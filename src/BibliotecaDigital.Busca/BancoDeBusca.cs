using Microsoft.Data.Sqlite;

namespace BibliotecaDigital.Busca;

/// <summary>
/// O índice de busca: um arquivo SQLite com FTS5. É derivado do acervo e pode ser apagado e
/// refeito a qualquer momento (<c>biblioteca reindexar</c>) — não guarda nada que não esteja
/// nos arquivos do acervo. Por isso não precisa de backup nem de servidor de banco.
/// </summary>
public static class BancoDeBusca
{
    public const int VersaoDoEsquema = 1;

    public static SqliteConnection Abrir(string arquivo, bool somenteLeitura = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arquivo))!);
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = arquivo,
            Mode = somenteLeitura ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        var c = new SqliteConnection(cs);
        c.Open();
        Executar(c, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;");
        return c;
    }

    public static void CriarEsquema(SqliteConnection c) => Executar(c, $"""
        CREATE TABLE IF NOT EXISTS meta (chave TEXT PRIMARY KEY, valor TEXT);
        INSERT OR REPLACE INTO meta VALUES ('esquema', '{VersaoDoEsquema}');

        CREATE TABLE IF NOT EXISTS documento (
            id INTEGER PRIMARY KEY,
            chave TEXT NOT NULL UNIQUE,
            colecao TEXT NOT NULL,
            titulo TEXT NOT NULL,
            ano INTEGER,
            campos TEXT NOT NULL,
            pasta TEXT NOT NULL,
            nome_base TEXT NOT NULL,
            paginas INTEGER NOT NULL,
            url_publica TEXT,
            ocr TEXT
        );
        CREATE INDEX IF NOT EXISTS documento_colecao_ano ON documento(colecao, ano);

        CREATE TABLE IF NOT EXISTS pagina (
            id INTEGER PRIMARY KEY,
            documento_id INTEGER NOT NULL REFERENCES documento(id) ON DELETE CASCADE,
            numero INTEGER NOT NULL,
            impressa TEXT,
            fidelidade TEXT NOT NULL,
            texto TEXT NOT NULL,
            margens TEXT NOT NULL,
            errata TEXT NOT NULL,
            UNIQUE (documento_id, numero)
        );

        CREATE VIRTUAL TABLE IF NOT EXISTS pagina_fts USING fts5(
            texto, margens, errata,
            content='pagina', content_rowid='id',
            tokenize="unicode61 remove_diacritics 2");

        CREATE VIRTUAL TABLE IF NOT EXISTS pagina_radical USING fts5(
            radicais, tokenize="unicode61 remove_diacritics 2");

        CREATE TABLE IF NOT EXISTS termo (
            id INTEGER PRIMARY KEY,
            termo TEXT NOT NULL UNIQUE,
            ocorrencias INTEGER NOT NULL
        );
        CREATE VIRTUAL TABLE IF NOT EXISTS termo_tri USING fts5(
            termo, content='termo', content_rowid='id', tokenize='trigram');

        CREATE TABLE IF NOT EXISTS artigo (
            id INTEGER PRIMARY KEY,
            documento_id INTEGER NOT NULL REFERENCES documento(id) ON DELETE CASCADE,
            titulo TEXT NOT NULL,
            autor TEXT,
            secao TEXT,
            paginas TEXT,
            pdf_inicio INTEGER,
            pdf_fim INTEGER
        );
        CREATE VIRTUAL TABLE IF NOT EXISTS artigo_fts USING fts5(
            titulo, autor, secao,
            content='artigo', content_rowid='id',
            tokenize="unicode61 remove_diacritics 2");
        """);

    public static void Executar(SqliteConnection c, string sql, params (string, object?)[] parametros)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static T? Escalar<T>(SqliteConnection c, string sql, params (string, object?)[] parametros)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? default : (T)Convert.ChangeType(r, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }
}
