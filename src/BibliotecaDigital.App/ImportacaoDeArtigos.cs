using System.Text.RegularExpressions;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.App;

/// <summary>
/// Traz para as fichas o catálogo de artigos feito pelos editores da revista (título, autor,
/// seção e páginas impressas), a partir de tabelas em Markdown (um arquivo por década, por
/// exemplo: data, tomo, título, seção, autor, páginas). Roda uma vez, na migração;
/// depois disso os artigos moram na ficha e se editam pela tela.
/// </summary>
public static partial class ImportacaoDeArtigos
{
    private static readonly string[] Meses =
        ["janeiro", "fevereiro", "marco", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    public sealed record Linha(int Mes, int Ano, string Tomo, string Titulo, string? Secao, string? Autor, string? Paginas, string Fonte);

    public static int Executar(RepositorioDoAcervo repo, string pasta, bool substituir)
    {
        var linhas = Directory.EnumerateFiles(pasta, "*.md").SelectMany(Ler).ToList();
        Console.Error.WriteLine($"{linhas.Count} artigos em {pasta}");

        var docs = repo.Documentos().Select(d => (Arquivos: d, Ficha: Ficha.Ler(d.Ficha),
            Impressas: File.Exists(d.Texto) ? BibliotecaDigital.Busca.Indexador.MapaDeImpressas(FormatoMarkdown.Ler(File.ReadAllText(d.Texto))).Keys.ToHashSet() : [])).ToList();
        var porEdicao = new Dictionary<string, List<Linha>>();
        var semEdicao = new List<Linha>();
        int porMes = 0, porPagina = 0, porNumero = 0;
        foreach (var original in linhas)
        {
            var l = original;
            var mesmoTomo = docs.Where(d =>
                    d.Ficha.Campos.GetValueOrDefault("ano") == l.Ano.ToString()
                    && Numeros.Valor(d.Ficha.Campos.GetValueOrDefault("tomo")) == Numeros.Valor(l.Tomo))
                .ToList();
            var candidatos = l.Mes > 0
                ? mesmoTomo.Where(d => MesesDe(d.Ficha.Campos.GetValueOrDefault("meses")).Contains(l.Mes)).ToList()
                : [];
            // 2009–2014: o catálogo não tem tomo, mas tem o número da edição na coluna de
            // páginas ("n.03 p.46"). Edição e página saem dali, exatas.
            if (candidatos.Count == 0 && NumeroEPagina().Match(l.Paginas ?? "") is { Success: true } np)
            {
                var numero = int.Parse(np.Groups[1].Value);
                candidatos = docs.Where(d => d.Ficha.Campos.GetValueOrDefault("ano") == l.Ano.ToString()
                    && Numeros.Cobertos(d.Ficha.Campos.GetValueOrDefault("numero")).Contains(numero)).ToList();
                if (candidatos.Count > 0) { porNumero++; l = l with { Paginas = np.Groups[2].Value }; }
            }
            if (candidatos.Count > 0) { if (l.Mes > 0) porMes++; }
            else
            {
                // Sem mês na linha (353 do catálogo só têm o ano) ou sem mês no nome do arquivo
                // (1966, 1968, 1999): decide a página impressa onde o artigo começa — se ela
                // existir em uma única edição daquele tomo. Em dúvida, fica sem associação.
                var pagina = PrimeiraPagina(l.Paginas);
                candidatos = pagina is { } n ? mesmoTomo.Where(d => d.Impressas.Contains(n)).ToList() : [];
                if (candidatos.Count != 1) { semEdicao.Add(l); continue; }
                porPagina++;
            }
            // Edição em duas partes (2007, n.11-12 Parte 1 e Parte 2): fica na primeira.
            var alvo = candidatos.OrderBy(d => d.Arquivos.NomeBase, StringComparer.Ordinal).First();
            (porEdicao.TryGetValue(alvo.Arquivos.Id, out var lista) ? lista : porEdicao[alvo.Arquivos.Id] = []).Add(l);
        }
        Console.Error.WriteLine($"  associados pelo mês: {porMes} · pelo número da edição: {porNumero} · pela página impressa: {porPagina}");

        var atualizadas = 0;
        foreach (var (id, artigos) in porEdicao)
        {
            var (arquivos, ficha, _) = docs.First(d => d.Arquivos.Id == id);
            if (ficha.Artigos.Count > 0 && !substituir) continue;
            var novos = artigos.Select(a => new Artigo { Titulo = a.Titulo, Autor = a.Autor, Secao = a.Secao, Paginas = a.Paginas, Fonte = a.Fonte }).ToList();
            var nova = ficha with
            {
                Artigos = novos,
                Historico = [.. ficha.Historico, new EventoDaFicha(DateTimeOffset.Now, "migração", "artigos importados",
                    $"{novos.Count} artigos do índice dos editores (índice importado)")],
            };
            nova.Gravar(arquivos.Ficha);
            atualizadas++;
        }

        Console.WriteLine($"{linhas.Count - semEdicao.Count} artigos associados a {porEdicao.Count} edições ({atualizadas} fichas gravadas) · {semEdicao.Count} sem edição correspondente");
        foreach (var g in semEdicao.GroupBy(l => $"{l.Mes:00}/{l.Ano} tomo {l.Tomo}").Take(25))
            Console.WriteLine($"  sem edição: {g.Key} ({g.Count()} artigos)");
        return 0;
    }

    public static IEnumerable<Linha> Ler(string arquivo)
    {
        var fonte = "Índice de artigos dos editores — " + Path.GetFileName(arquivo);
        foreach (var bruta in File.ReadLines(arquivo))
        {
            var m = LinhaDaTabela().Match(bruta);
            if (!m.Success) continue;
            var celulas = bruta.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (celulas.Count < 6) continue;
            var data = DataDoArtigo().Match(celulas[0]);
            if (!data.Success) continue;
            string? Nulo(string s) => string.IsNullOrWhiteSpace(s) || s.Equals("Não cita autor", StringComparison.OrdinalIgnoreCase) ? null : s;
            var mes = data.Groups[1].Success ? int.Parse(data.Groups[1].Value) : 0;
            yield return new Linha(mes, int.Parse(data.Groups[2].Value), celulas[1],
                celulas[2], Nulo(celulas[3]), Nulo(celulas[4]), Nulo(celulas[5]), fonte);
        }
    }

    [GeneratedRegex(@"n\.?\s*0*(\d+)\s*p\.?\s*(\d+(?:\s*-\s*\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex NumeroEPagina();

    /// <summary>A primeira página da faixa impressa ("12-14" → 12).</summary>
    public static int? PrimeiraPagina(string? paginas)
    {
        var m = Regex.Match(paginas ?? "", @"\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    /// <summary>Números dos meses citados ("Janeiro-fevereiro" → 1, 2; "Junho-Outubro" → 6..10).</summary>
    public static HashSet<int> MesesDe(string? texto)
    {
        var achados = Tokens(texto).Select(t => Array.IndexOf(Meses, t) + 1).Where(n => n > 0).ToList();
        if (achados.Count == 2 && achados[1] > achados[0] + 1 && texto!.Contains('-'))
            return Enumerable.Range(achados[0], achados[1] - achados[0] + 1).ToHashSet();
        return achados.ToHashSet();
    }

    private static IEnumerable<string> Tokens(string? s) =>
        Regex.Split(Texto.Dobrado(s ?? ""), @"[^a-z]+").Where(t => t.Length > 0);

    [GeneratedRegex(@"^\|\s*(\d{1,2}/)?\d{4}\s*\|")]
    private static partial Regex LinhaDaTabela();

    [GeneratedRegex(@"^(?:(\d{1,2})/)?(\d{4})$")]
    private static partial Regex DataDoArtigo();
}
