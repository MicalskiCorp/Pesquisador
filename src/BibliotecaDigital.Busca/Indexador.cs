using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Busca;

public sealed record ResultadoDaIndexacao(int Documentos, int Paginas, int Termos, int Artigos, TimeSpan Duracao, IReadOnlyList<string> Erros);

/// <summary>
/// Acervo → índice. Lê só os arquivos do acervo (ficha e <c>.md</c>); nunca o PDF. A
/// reconstrução completa grava num arquivo novo e troca no fim, para a busca não parar.
/// </summary>
public sealed class Indexador(RepositorioDoAcervo repositorio)
{
    public ResultadoDaIndexacao ReconstruirTudo(string arquivoDoBanco, Action<int, int>? progresso = null)
    {
        var relogio = Stopwatch.StartNew();
        var novo = arquivoDoBanco + ".novo";
        foreach (var f in new[] { novo, novo + "-wal", novo + "-shm" }) if (File.Exists(f)) File.Delete(f);

        var erros = new List<string>();
        int docs = 0, paginas = 0, artigos = 0, termos;
        var vocabulario = new Dictionary<string, int>(StringComparer.Ordinal);
        var colecoes = repositorio.Colecoes().ToDictionary(c => c.Id);
        var lista = repositorio.Documentos().ToList();

        using (var c = BancoDeBusca.Abrir(novo))
        {
            BancoDeBusca.CriarEsquema(c);
            using (var tx = c.BeginTransaction())
            {
                foreach (var d in lista)
                {
                    try
                    {
                        var (p, a) = Inserir(c, d, colecoes, vocabulario, +1);
                        docs++; paginas += p; artigos += a;
                    }
                    catch (Exception e) { erros.Add($"{d.Id}: {e.Message}"); }
                    progresso?.Invoke(docs + erros.Count, lista.Count);
                }
                termos = GravarVocabulario(c, vocabulario);
                tx.Commit();
            }
            BancoDeBusca.Executar(c, "INSERT INTO pagina_fts(pagina_fts) VALUES('optimize'); INSERT INTO pagina_radical(pagina_radical) VALUES('optimize');");
            BancoDeBusca.Executar(c, "PRAGMA wal_checkpoint(TRUNCATE);");
        }
        SqliteConnection.ClearAllPools();
        Trocar(novo, arquivoDoBanco);
        return new ResultadoDaIndexacao(docs, paginas, termos, artigos, relogio.Elapsed, erros);
    }

    /// <summary>Reindexa um documento só — depois de incluir, editar a ficha ou registrar errata.</summary>
    public void IndexarDocumento(string arquivoDoBanco, ArquivosDoDocumento doc)
    {
        if (!File.Exists(arquivoDoBanco)) { ReconstruirTudo(arquivoDoBanco); return; }
        var colecoes = repositorio.Colecoes().ToDictionary(c => c.Id);
        using var c = BancoDeBusca.Abrir(arquivoDoBanco);
        using var tx = c.BeginTransaction();
        var vocabulario = new Dictionary<string, int>(StringComparer.Ordinal);
        Remover(c, doc.Id, vocabulario);
        if (File.Exists(doc.Ficha) && File.Exists(doc.Texto))
            Inserir(c, doc, colecoes, vocabulario, +1);
        AjustarVocabulario(c, vocabulario);
        tx.Commit();
    }

    public void RemoverDocumento(string arquivoDoBanco, string id)
    {
        using var c = BancoDeBusca.Abrir(arquivoDoBanco);
        using var tx = c.BeginTransaction();
        var vocabulario = new Dictionary<string, int>(StringComparer.Ordinal);
        Remover(c, id, vocabulario);
        AjustarVocabulario(c, vocabulario);
        tx.Commit();
    }

    private static (int Paginas, int Artigos) Inserir(SqliteConnection c, ArquivosDoDocumento d,
        Dictionary<string, Colecao> colecoes, Dictionary<string, int> vocabulario, int sinal)
    {
        var ficha = Ficha.Ler(d.Ficha);
        var texto = FormatoMarkdown.Ler(File.ReadAllText(d.Texto));
        var carimbos = colecoes.TryGetValue(ficha.Colecao, out var col)
            ? col.Extracao.Margens.Select(m => new Regex(m, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray()
            : [];
        int? ano = col?.Ano(ficha.Campos) ?? (ficha.Campos.TryGetValue("ano", out var a) && int.TryParse(a, out var n) ? n : null);

        long docId;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO documento (chave, colecao, titulo, ano, campos, pasta, nome_base, paginas, url_publica, ocr)
                VALUES ($chave, $colecao, $titulo, $ano, $campos, $pasta, $nome, $paginas, $url, $ocr) RETURNING id
                """;
            cmd.Parameters.AddWithValue("$chave", d.Id);
            cmd.Parameters.AddWithValue("$colecao", ficha.Colecao);
            cmd.Parameters.AddWithValue("$titulo", ficha.Titulo);
            cmd.Parameters.AddWithValue("$ano", (object?)ano ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$campos", JsonSerializer.Serialize(ficha.Campos));
            cmd.Parameters.AddWithValue("$pasta", d.Pasta);
            cmd.Parameters.AddWithValue("$nome", d.NomeBase);
            cmd.Parameters.AddWithValue("$paginas", texto.Paginas.Count);
            cmd.Parameters.AddWithValue("$url", (object?)ficha.Pdf.UrlPublica ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ocr", (object?)ficha.Pdf.Produtor ?? DBNull.Value);
            docId = (long)cmd.ExecuteScalar()!;
        }

        var errataPorPagina = ficha.Errata.GroupBy(e => e.Pagina).ToDictionary(g => g.Key, g => string.Join('\n', g.Select(e => e.Leitura)));
        using var insPagina = c.CreateCommand();
        insPagina.CommandText = """
            INSERT INTO pagina (documento_id, numero, impressa, fidelidade, texto, margens, errata)
            VALUES ($doc, $num, $imp, $fid, $texto, $margens, $errata) RETURNING id
            """;
        using var insFts = c.CreateCommand();
        insFts.CommandText = "INSERT INTO pagina_fts(rowid, texto, margens, errata) VALUES ($id, $texto, $margens, $errata)";
        using var insRad = c.CreateCommand();
        insRad.CommandText = "INSERT INTO pagina_radical(rowid, radicais) VALUES ($id, $radicais)";

        foreach (var p in texto.Paginas)
        {
            var corpo = p.Corpo;
            // O carimbo de digitalização está em toda página e não aponta para lugar nenhum.
            var margens = string.Join('\n', p.Margens.Where(m => !carimbos.Any(r => r.IsMatch(m))));
            var errata = errataPorPagina.GetValueOrDefault(p.Numero, "");

            insPagina.Parameters.Clear();
            insPagina.Parameters.AddWithValue("$doc", docId);
            insPagina.Parameters.AddWithValue("$num", p.Numero);
            insPagina.Parameters.AddWithValue("$imp", (object?)p.Impressa ?? DBNull.Value);
            insPagina.Parameters.AddWithValue("$fid", FormatoMarkdown.Nome(p.Fidelidade));
            insPagina.Parameters.AddWithValue("$texto", corpo);
            insPagina.Parameters.AddWithValue("$margens", margens);
            insPagina.Parameters.AddWithValue("$errata", errata);
            var pid = (long)insPagina.ExecuteScalar()!;

            insFts.Parameters.Clear();
            insFts.Parameters.AddWithValue("$id", pid);
            insFts.Parameters.AddWithValue("$texto", corpo);
            insFts.Parameters.AddWithValue("$margens", margens);
            insFts.Parameters.AddWithValue("$errata", errata);
            insFts.ExecuteNonQuery();

            insRad.Parameters.Clear();
            insRad.Parameters.AddWithValue("$id", pid);
            insRad.Parameters.AddWithValue("$radicais", RadicalPortugues.Radicais(corpo + "\n" + margens + "\n" + errata));
            insRad.ExecuteNonQuery();

            foreach (var t in Tokens.Dobrados(corpo + "\n" + margens + "\n" + errata))
                if (t.Length >= 2) vocabulario[t] = vocabulario.GetValueOrDefault(t) + sinal;
        }

        var artigos = InserirArtigos(c, docId, ficha, texto);
        return (texto.Paginas.Count, artigos);
    }

    private static int InserirArtigos(SqliteConnection c, long docId, Ficha ficha, DocumentoTexto texto)
    {
        if (ficha.Artigos.Count == 0) return 0;
        var mapa = MapaDeImpressas(texto);
        using var ins = c.CreateCommand();
        ins.CommandText = """
            INSERT INTO artigo (documento_id, titulo, autor, secao, paginas, pdf_inicio, pdf_fim)
            VALUES ($doc, $titulo, $autor, $secao, $paginas, $ini, $fim) RETURNING id
            """;
        using var fts = c.CreateCommand();
        fts.CommandText = "INSERT INTO artigo_fts(rowid, titulo, autor, secao) VALUES ($id, $titulo, $autor, $secao)";
        foreach (var a in ficha.Artigos)
        {
            var (ini, fim) = PaginasDoPdf(a.Paginas, mapa);
            ins.Parameters.Clear();
            ins.Parameters.AddWithValue("$doc", docId);
            ins.Parameters.AddWithValue("$titulo", a.Titulo);
            ins.Parameters.AddWithValue("$autor", (object?)a.Autor ?? DBNull.Value);
            ins.Parameters.AddWithValue("$secao", (object?)a.Secao ?? DBNull.Value);
            ins.Parameters.AddWithValue("$paginas", (object?)a.Paginas ?? DBNull.Value);
            ins.Parameters.AddWithValue("$ini", (object?)ini ?? DBNull.Value);
            ins.Parameters.AddWithValue("$fim", (object?)fim ?? DBNull.Value);
            var id = (long)ins.ExecuteScalar()!;
            fts.Parameters.Clear();
            fts.Parameters.AddWithValue("$id", id);
            fts.Parameters.AddWithValue("$titulo", a.Titulo);
            fts.Parameters.AddWithValue("$autor", a.Autor ?? "");
            fts.Parameters.AddWithValue("$secao", a.Secao ?? "");
            fts.ExecuteNonQuery();
        }
        return ficha.Artigos.Count;
    }

    /// <summary>Página impressa → página do PDF. Página dupla ("36-37") responde pelos dois números.</summary>
    public static Dictionary<int, int> MapaDeImpressas(DocumentoTexto texto)
    {
        var mapa = new Dictionary<int, int>();
        foreach (var p in texto.Paginas)
        {
            if (p.Impressa is null) continue;
            foreach (var parte in p.Impressa.Split('-', StringSplitOptions.TrimEntries))
                if (int.TryParse(parte, out var n)) mapa.TryAdd(n, p.Numero);
        }
        return mapa;
    }

    /// <summary>"2-3" (impressas) → (4, 5) (do PDF), quando o mapa permite.</summary>
    public static (int?, int?) PaginasDoPdf(string? impressas, IReadOnlyDictionary<int, int> mapa)
    {
        if (string.IsNullOrWhiteSpace(impressas)) return (null, null);
        var numeros = Regex.Matches(impressas, @"\d+").Select(m => int.Parse(m.Value)).ToList();
        if (numeros.Count == 0) return (null, null);
        int? ini = mapa.TryGetValue(numeros[0], out var i) ? i : null;
        int? fim = mapa.TryGetValue(numeros[^1], out var f) ? f : ini;
        return (ini, fim);
    }

    private static void Remover(SqliteConnection c, string chave, Dictionary<string, int> vocabulario)
    {
        var docId = BancoDeBusca.Escalar<long?>(c, "SELECT id FROM documento WHERE chave = $c", ("$c", chave));
        if (docId is null) return;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, texto, margens, errata FROM pagina WHERE documento_id = $d";
            cmd.Parameters.AddWithValue("$d", docId);
            using var r = cmd.ExecuteReader();
            var antigas = new List<(long, string, string, string)>();
            while (r.Read()) antigas.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
            r.Close();
            foreach (var (id, texto, margens, errata) in antigas)
            {
                BancoDeBusca.Executar(c, "INSERT INTO pagina_fts(pagina_fts, rowid, texto, margens, errata) VALUES('delete', $id, $t, $m, $e)",
                    ("$id", id), ("$t", texto), ("$m", margens), ("$e", errata));
                BancoDeBusca.Executar(c, "DELETE FROM pagina_radical WHERE rowid = $id", ("$id", id));
                foreach (var t in Tokens.Dobrados(texto + "\n" + margens + "\n" + errata))
                    if (t.Length >= 2) vocabulario[t] = vocabulario.GetValueOrDefault(t) - 1;
            }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, titulo, coalesce(autor,''), coalesce(secao,'') FROM artigo WHERE documento_id = $d";
            cmd.Parameters.AddWithValue("$d", docId);
            using var r = cmd.ExecuteReader();
            var antigos = new List<(long, string, string, string)>();
            while (r.Read()) antigos.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
            r.Close();
            foreach (var (id, t, a, s) in antigos)
                BancoDeBusca.Executar(c, "INSERT INTO artigo_fts(artigo_fts, rowid, titulo, autor, secao) VALUES('delete', $id, $t, $a, $s)",
                    ("$id", id), ("$t", t), ("$a", a), ("$s", s));
        }
        BancoDeBusca.Executar(c, "DELETE FROM documento WHERE id = $d", ("$d", docId));
    }

    private static int GravarVocabulario(SqliteConnection c, Dictionary<string, int> vocabulario)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO termo (termo, ocorrencias) VALUES ($t, $n)";
        var pt = cmd.Parameters.Add("$t", SqliteType.Text);
        var pn = cmd.Parameters.Add("$n", SqliteType.Integer);
        var total = 0;
        foreach (var (t, n) in vocabulario)
        {
            if (n <= 0 || t.All(char.IsDigit)) continue;
            pt.Value = t; pn.Value = n;
            cmd.ExecuteNonQuery();
            total++;
        }
        BancoDeBusca.Executar(c, "INSERT INTO termo_tri(termo_tri) VALUES('rebuild')");
        return total;
    }

    private static void AjustarVocabulario(SqliteConnection c, Dictionary<string, int> delta)
    {
        foreach (var (t, n) in delta)
        {
            if (n == 0 || t.All(char.IsDigit)) continue;
            var atual = BancoDeBusca.Escalar<long?>(c, "SELECT id FROM termo WHERE termo = $t", ("$t", t));
            if (atual is null)
            {
                if (n <= 0) continue;
                var id = BancoDeBusca.Escalar<long>(c, "INSERT INTO termo (termo, ocorrencias) VALUES ($t, $n) RETURNING id", ("$t", t), ("$n", n));
                BancoDeBusca.Executar(c, "INSERT INTO termo_tri(rowid, termo) VALUES ($id, $t)", ("$id", id), ("$t", t));
            }
            else BancoDeBusca.Executar(c, "UPDATE termo SET ocorrencias = max(0, ocorrencias + $n) WHERE id = $id", ("$n", n), ("$id", atual));
        }
    }

    private static void Trocar(string novo, string destino)
    {
        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                foreach (var f in new[] { destino + "-wal", destino + "-shm" }) if (File.Exists(f)) File.Delete(f);
                File.Move(novo, destino, overwrite: true);
                return;
            }
            catch (IOException) when (tentativa < 20) { Thread.Sleep(250); }
        }
    }
}
