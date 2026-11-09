using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace BibliotecaDigital.Busca;

public sealed record Pedido(
    string Texto,
    Modo Modo = Modo.Exato,
    string? Colecao = null,
    int? AnoDe = null,
    int? AnoAte = null,
    string? Documento = null,
    string? Autor = null,
    IReadOnlyCollection<string>? VariantesExcluidas = null,
    int Pagina = 1,
    int PorPagina = 20);

public sealed record Segmento(string Texto, bool Destaque);
public sealed record Trecho(IReadOnlyList<Segmento> Segmentos);
public sealed record ArtigoDaPagina(string Titulo, string? Autor);
public sealed record PaginaEncontrada(int Numero, string? Impressa, IReadOnlyList<Trecho> Trechos, IReadOnlyList<ArtigoDaPagina> Artigos);
public sealed record DocumentoEncontrado(
    string Id, string Titulo, string Colecao, int? Ano, int TotalDePaginas,
    IReadOnlyList<int> PaginasComOcorrencia, IReadOnlyList<PaginaEncontrada> Destaques, string? UrlPublica);
public sealed record Variante(string Termo, string Grafia, int Ocorrencias, bool Incluida);
public sealed record ArtigoEncontrado(string Documento, string TituloDoDocumento, string Titulo, string? Autor, string? Secao, string? Paginas, int? PaginaDoPdf);

public sealed record ResultadoDaBusca(
    string Consulta, Modo Modo, int Documentos, int Paginas,
    IReadOnlyList<DocumentoEncontrado> Itens, IReadOnlyList<Variante> Variantes,
    IReadOnlyList<ArtigoEncontrado> Artigos, double Milissegundos, string? Aviso);

/// <summary>
/// A busca. Unidade: a página. Resultado: documentos, cada um com as páginas em que o termo
/// aparece, o número impresso, o artigo e o trecho com destaque. Nenhuma etapa usa modelo
/// treinado: índice invertido (FTS5), regra fixa de radical e distância de edição.
/// </summary>
public sealed class MotorDeBusca(string arquivoDoBanco)
{
    private const int LimiteDePaginas = 5000;
    private const int DestaquesPorDocumento = 3;

    public bool Disponivel => File.Exists(arquivoDoBanco);

    public ResultadoDaBusca Buscar(Pedido pedido)
    {
        var relogio = Stopwatch.StartNew();
        var termos = Consulta.Interpretar(pedido.Texto);
        if (termos.All(t => t.Negado))
            return Vazio(pedido, relogio, "Digite ao menos uma palavra para procurar.");
        if (!Disponivel)
            return Vazio(pedido, relogio, "O índice de busca ainda não foi gerado. Rode: biblioteca reindexar");

        using var c = BancoDeBusca.Abrir(arquivoDoBanco, somenteLeitura: true);
        var variantes = new List<Variante>();
        string? fts;
        string tabela;
        Func<string, bool> casa;

        switch (pedido.Modo)
        {
            case Modo.Variacoes:
                tabela = "pagina_radical";
                fts = Consulta.ParaFts(termos, t => t.Frase
                    ? Consulta.Citar(string.Join(' ', t.Palavras.Select(RadicalPortugues.Radical)))
                    : Consulta.Citar(t.Prefixo ? t.Palavras[0] : RadicalPortugues.Radical(t.Palavras[0])) + (t.Prefixo ? "*" : ""));
                var radicais = termos.Where(t => !t.Negado && !t.Prefixo).SelectMany(t => t.Palavras).Select(RadicalPortugues.Radical).ToHashSet();
                var prefixosV = Prefixos(termos);
                casa = p => radicais.Contains(RadicalPortugues.Radical(p)) || prefixosV.Any(p.StartsWith);
                break;

            case Modo.Tolerante:
                tabela = "pagina_fts";
                var excluidas = new HashSet<string>(pedido.VariantesExcluidas ?? [], StringComparer.Ordinal);
                var porPalavra = new Dictionary<string, List<string>>();
                foreach (var palavra in termos.Where(t => !t.Negado && !t.Prefixo).SelectMany(t => t.Palavras).Distinct())
                {
                    var encontradas = Variantes(c, palavra);
                    variantes.AddRange(encontradas.Select(v => v with { Incluida = !excluidas.Contains(v.Grafia) }));
                    var usadas = encontradas.Where(v => !excluidas.Contains(v.Grafia)).Select(v => v.Grafia).ToList();
                    if (!usadas.Contains(palavra) && !excluidas.Contains(palavra)) usadas.Insert(0, palavra);
                    porPalavra[palavra] = usadas.Count > 0 ? usadas : [palavra];
                }
                fts = Consulta.ParaFts(termos, t => ExpressaoTolerante(t, porPalavra));
                var todas = porPalavra.Values.SelectMany(v => v).ToHashSet();
                var prefixosT = Prefixos(termos);
                casa = p => todas.Contains(p) || prefixosT.Any(p.StartsWith);
                break;

            default:
                tabela = "pagina_fts";
                fts = Consulta.ParaFts(termos, Consulta.Exata);
                var exatas = termos.Where(t => !t.Negado && !t.Prefixo).SelectMany(t => t.Palavras).ToHashSet();
                var prefixosE = Prefixos(termos);
                casa = p => exatas.Contains(p) || prefixosE.Any(p.StartsWith);
                break;
        }
        if (fts is null) return Vazio(pedido, relogio, "Digite ao menos uma palavra para procurar.");

        var achadas = new List<(long Pagina, long Documento, int Numero, string? Impressa, double Rank)>();
        using (var cmd = c.CreateCommand())
        {
            var filtros = Filtros(cmd, pedido);
            cmd.CommandText = $"""
                SELECT p.id, p.documento_id, p.numero, p.impressa, bm25({tabela}) AS r
                FROM {tabela} JOIN pagina p ON p.id = {tabela}.rowid JOIN documento d ON d.id = p.documento_id
                WHERE {tabela} MATCH $q {filtros}
                ORDER BY r LIMIT {LimiteDePaginas}
                """;
            cmd.Parameters.AddWithValue("$q", fts);
            try
            {
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    achadas.Add((r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDouble(4)));
            }
            catch (SqliteException e)
            {
                return Vazio(pedido, relogio, $"Não consegui interpretar a busca ({e.SqliteErrorCode}). Tente sem símbolos.");
            }
        }

        var grupos = achadas.GroupBy(a => a.Documento)
            .Select(g => (Documento: g.Key, Melhor: g.Min(x => x.Rank), Paginas: g.OrderBy(x => x.Rank).ToList()))
            .OrderBy(g => g.Melhor)
            .ToList();

        var pagina = grupos.Skip((Math.Max(1, pedido.Pagina) - 1) * pedido.PorPagina).Take(pedido.PorPagina).ToList();
        var itens = new List<DocumentoEncontrado>();
        foreach (var g in pagina)
        {
            var doc = LerDocumento(c, g.Documento);
            var destaques = g.Paginas.Take(DestaquesPorDocumento)
                .Select(p => new PaginaEncontrada(p.Numero, p.Impressa, Trechos(c, p.Pagina, casa), ArtigosDaPagina(c, g.Documento, p.Numero)))
                .ToList();
            itens.Add(doc with
            {
                PaginasComOcorrencia = g.Paginas.Select(p => p.Numero).Order().ToList(),
                Destaques = destaques,
            });
        }

        var artigos = pedido.Pagina <= 1 ? BuscarArtigos(c, termos, pedido) : [];
        var aviso = achadas.Count >= LimiteDePaginas ? $"Mais de {LimiteDePaginas} páginas: refine a busca para ver todas." : null;
        return new ResultadoDaBusca(pedido.Texto, pedido.Modo, grupos.Count, achadas.Count, itens, variantes, artigos, relogio.Elapsed.TotalMilliseconds, aviso);
    }

    /// <summary>
    /// Diz quais palavras de um texto casam com a busca, no mesmo critério da busca — é o que
    /// marca o termo no texto da página do documento.
    /// </summary>
    public Func<string, bool> Realcador(string texto, Modo modo, IReadOnlyCollection<string>? excluidas = null)
    {
        var termos = Consulta.Interpretar(texto);
        var palavras = termos.Where(t => !t.Negado && !t.Prefixo).SelectMany(t => t.Palavras).ToList();
        var prefixos = Prefixos(termos);
        if (modo == Modo.Variacoes)
        {
            var radicais = palavras.Select(RadicalPortugues.Radical).ToHashSet();
            return p => radicais.Contains(RadicalPortugues.Radical(p)) || prefixos.Any(p.StartsWith);
        }
        var aceitas = palavras.ToHashSet();
        if (modo == Modo.Tolerante && Disponivel)
        {
            var fora = new HashSet<string>(excluidas ?? [], StringComparer.Ordinal);
            using var c = BancoDeBusca.Abrir(arquivoDoBanco, somenteLeitura: true);
            foreach (var p in palavras.Distinct())
                aceitas.UnionWith(Variantes(c, p).Select(v => v.Grafia).Where(g => !fora.Contains(g)));
            aceitas.ExceptWith(fora);
        }
        return p => aceitas.Contains(p) || prefixos.Any(p.StartsWith);
    }

    /// <summary>
    /// As grafias do acervo parecidas com a palavra digitada, segundo a distância de OCR. Sai
    /// com a contagem, para o biblioteca ver o que está sendo incluído e desmarcar o que não quer.
    /// </summary>
    public List<Variante> Variantes(SqliteConnection c, string palavra, int maximo = 12)
    {
        if (palavra.Length < 3) return Exata(c, palavra);
        var trigramas = Enumerable.Range(0, palavra.Length - 2).Select(i => palavra.Substring(i, 3)).Distinct().ToList();
        var candidatos = new List<(string Termo, int Ocorrencias)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT t.termo, t.ocorrencias FROM termo_tri JOIN termo t ON t.id = termo_tri.rowid
                WHERE termo_tri MATCH $q AND length(t.termo) BETWEEN $min AND $max
                ORDER BY bm25(termo_tri) LIMIT 800
                """;
            cmd.Parameters.AddWithValue("$q", string.Join(" OR ", trigramas.Select(Consulta.Citar)));
            cmd.Parameters.AddWithValue("$min", Math.Max(2, palavra.Length - 3));
            cmd.Parameters.AddWithValue("$max", palavra.Length + 3);
            using var r = cmd.ExecuteReader();
            while (r.Read()) candidatos.Add((r.GetString(0), r.GetInt32(1)));
        }
        var limite = DistanciaOcr.Tolerancia(palavra.Length);
        return candidatos
            .Select(x => (x.Termo, x.Ocorrencias, Distancia: DistanciaOcr.Calcular(palavra, x.Termo, limite)))
            .Where(x => x.Distancia <= limite)
            .OrderBy(x => x.Distancia).ThenByDescending(x => x.Ocorrencias)
            .Take(maximo)
            .Select(x => new Variante(palavra, x.Termo, x.Ocorrencias, true))
            .ToList();
    }

    private static List<Variante> Exata(SqliteConnection c, string palavra)
    {
        var n = BancoDeBusca.Escalar<long?>(c, "SELECT ocorrencias FROM termo WHERE termo = $t", ("$t", palavra));
        return n is null ? [] : [new Variante(palavra, palavra, (int)n, true)];
    }

    private static string ExpressaoTolerante(Termo t, Dictionary<string, List<string>> porPalavra)
    {
        if (t.Prefixo || t.Negado) return Consulta.Exata(t);
        if (!t.Frase) return "(" + string.Join(" OR ", porPalavra[t.Palavras[0]].Select(Consulta.Citar)) + ")";
        // Frase: combinações das grafias, até 40 — além disso a frase fica só com a forma digitada.
        IEnumerable<List<string>> combinacoes = [[]];
        foreach (var p in t.Palavras)
            combinacoes = combinacoes.SelectMany(c => porPalavra.GetValueOrDefault(p, [p]).Select(v => new List<string>(c) { v }));
        var lista = combinacoes.Take(41).ToList();
        if (lista.Count > 40) lista = [t.Palavras.ToList()];
        return "(" + string.Join(" OR ", lista.Select(l => Consulta.Citar(string.Join(' ', l)))) + ")";
    }

    private static List<string> Prefixos(List<Termo> termos) =>
        termos.Where(t => t.Prefixo && !t.Negado).Select(t => t.Palavras[0]).ToList();

    private static string Filtros(SqliteCommand cmd, Pedido p)
    {
        var f = "";
        if (p.Colecao is { Length: > 0 }) { f += " AND d.colecao = $colecao"; cmd.Parameters.AddWithValue("$colecao", p.Colecao); }
        if (p.AnoDe is { } de) { f += " AND d.ano >= $de"; cmd.Parameters.AddWithValue("$de", de); }
        if (p.AnoAte is { } ate) { f += " AND d.ano <= $ate"; cmd.Parameters.AddWithValue("$ate", ate); }
        if (p.Documento is { Length: > 0 }) { f += " AND d.chave = $doc"; cmd.Parameters.AddWithValue("$doc", p.Documento); }
        if (p.Autor is { Length: > 0 })
        {
            f += """
                 AND EXISTS (SELECT 1 FROM artigo a WHERE a.documento_id = d.id AND a.autor LIKE $autor
                     AND (a.pdf_inicio IS NULL OR p.numero BETWEEN a.pdf_inicio AND coalesce(a.pdf_fim, a.pdf_inicio)))
                 """;
            cmd.Parameters.AddWithValue("$autor", "%" + p.Autor.Trim() + "%");
        }
        return f;
    }

    private static DocumentoEncontrado LerDocumento(SqliteConnection c, long id)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT chave, titulo, colecao, ano, paginas, url_publica FROM documento WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new DocumentoEncontrado(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3),
            r.GetInt32(4), [], [], r.IsDBNull(5) ? null : r.GetString(5));
    }

    private static List<ArtigoDaPagina> ArtigosDaPagina(SqliteConnection c, long documento, int numero)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT titulo, autor FROM artigo
            WHERE documento_id = $d AND pdf_inicio IS NOT NULL AND $n BETWEEN pdf_inicio AND coalesce(pdf_fim, pdf_inicio)
            ORDER BY pdf_inicio LIMIT 3
            """;
        cmd.Parameters.AddWithValue("$d", documento);
        cmd.Parameters.AddWithValue("$n", numero);
        using var r = cmd.ExecuteReader();
        var lista = new List<ArtigoDaPagina>();
        while (r.Read()) lista.Add(new ArtigoDaPagina(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
        return lista;
    }

    /// <summary>Até dois trechos por página, cada um com ~12 palavras de cada lado do achado.</summary>
    private static List<Trecho> Trechos(SqliteConnection c, long paginaId, Func<string, bool> casa)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT texto || char(10) || margens || char(10) || errata FROM pagina WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", paginaId);
        var texto = (string)cmd.ExecuteScalar()!;
        return GeradorDeTrechos.Gerar(texto, casa);
    }

    private static List<ArtigoEncontrado> BuscarArtigos(SqliteConnection c, List<Termo> termos, Pedido pedido)
    {
        var fts = Consulta.ParaFts(termos, Consulta.Exata);
        if (fts is null) return [];
        using var cmd = c.CreateCommand();
        var filtros = Filtros(cmd, pedido with { Autor = null }).Replace("p.numero", "a.pdf_inicio");
        cmd.CommandText = $"""
            SELECT d.chave, d.titulo, a.titulo, a.autor, a.secao, a.paginas, a.pdf_inicio
            FROM artigo_fts JOIN artigo a ON a.id = artigo_fts.rowid JOIN documento d ON d.id = a.documento_id
            WHERE artigo_fts MATCH $q {filtros}
            ORDER BY bm25(artigo_fts) LIMIT 15
            """;
        cmd.Parameters.AddWithValue("$q", fts);
        var lista = new List<ArtigoEncontrado>();
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new ArtigoEncontrado(r.GetString(0), r.GetString(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt32(6)));
        }
        catch (SqliteException) { }
        return lista;
    }

    private static ResultadoDaBusca Vazio(Pedido p, Stopwatch relogio, string aviso) =>
        new(p.Texto, p.Modo, 0, 0, [], [], [], relogio.Elapsed.TotalMilliseconds, aviso);
}

/// <summary>Recorta o texto da página em volta das palavras que casaram, marcando cada uma.</summary>
public static class GeradorDeTrechos
{
    public static List<Trecho> Gerar(string texto, Func<string, bool> casa, int contexto = 12, int maximo = 2)
    {
        var tokens = Tokens.Ler(texto);
        var achados = Enumerable.Range(0, tokens.Count).Where(i => casa(tokens[i].Dobrado)).ToList();
        if (achados.Count == 0)
            return tokens.Count == 0 ? [] : [new Trecho([new Segmento(Recortar(texto, 0, tokens[Math.Min(tokens.Count - 1, 2 * contexto)].Fim) + " …", false)])];

        var janelas = new List<(int Ini, int Fim)>();
        foreach (var i in achados)
        {
            var ini = Math.Max(0, i - contexto); var fim = Math.Min(tokens.Count - 1, i + contexto);
            if (janelas.Count > 0 && ini <= janelas[^1].Fim + 2) janelas[^1] = (janelas[^1].Ini, fim);
            else if (janelas.Count < maximo) janelas.Add((ini, fim));
        }

        var marcados = achados.ToHashSet();
        var trechos = new List<Trecho>();
        foreach (var (ini, fim) in janelas)
        {
            var segs = new List<Segmento>();
            var cursor = tokens[ini].Inicio;
            if (ini > 0) segs.Add(new Segmento("… ", false));
            for (var k = ini; k <= fim; k++)
            {
                if (!marcados.Contains(k)) continue;
                if (tokens[k].Inicio > cursor) segs.Add(new Segmento(Recortar(texto, cursor, tokens[k].Inicio), false));
                segs.Add(new Segmento(texto[tokens[k].Inicio..tokens[k].Fim], true));
                cursor = tokens[k].Fim;
            }
            if (tokens[fim].Fim > cursor) segs.Add(new Segmento(Recortar(texto, cursor, tokens[fim].Fim), false));
            if (fim < tokens.Count - 1) segs.Add(new Segmento(" …", false));
            trechos.Add(new Trecho(segs));
        }
        return trechos;
    }

    private static string Recortar(string texto, int ini, int fim) => texto[ini..fim].Replace('\n', ' ');
}
