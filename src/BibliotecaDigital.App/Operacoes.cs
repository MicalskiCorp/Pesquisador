using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.App;

/// <summary>Operações em lote sobre o acervo, usadas pela linha de comando.</summary>
public static partial class Operacoes
{
    /// <summary>Garante <c>colecoes.json</c> no acervo, copiando o modelo de <c>config/</c>.</summary>
    public static RepositorioDoAcervo PrepararAcervo(Configuracao cfg)
    {
        Directory.CreateDirectory(cfg.Acervo);
        var destino = Path.Combine(cfg.Acervo, RepositorioDoAcervo.ArquivoDeColecoes);
        if (!File.Exists(destino))
            ArquivoSeguro.Copiar(Path.Combine(cfg.Config, RepositorioDoAcervo.ArquivoDeColecoes), destino);
        return new RepositorioDoAcervo(cfg.Acervo);
    }

    public static Lexico? CarregarLexico(Configuracao cfg) => Lexico.Carregar(Path.Combine(cfg.Acervo, Lexico.Arquivo));

    /// <summary>Monta o léxico a partir de um conjunto de PDFs, em paralelo.</summary>
    public static Lexico MontarLexico(Configuracao cfg, IReadOnlyList<string> pdfs, Colecao colecao) =>
        MontarLexico(cfg, pdfs.Select(p => (p, colecao)).ToList());

    /// <summary>Monta o léxico de PDFs de uma ou mais coleções, cada um com as opções da sua.</summary>
    public static Lexico MontarLexico(Configuracao cfg, IReadOnlyList<(string Pdf, Colecao Colecao)> pdfs)
    {
        var poppler = new LeitorPoppler(cfg.Poppler);
        var parciais = new ConcurrentBag<ColetorDeLexico>();
        var feitos = 0;
        Parallel.ForEach(pdfs, new ParallelOptions { MaxDegreeOfParallelism = Paralelismo },
            () => new ColetorDeLexico(),
            (item, _, coletor) =>
            {
                var pdf = item.Pdf;
                try { coletor.Contar(poppler.Ler(pdf), item.Colecao.Extracao.DesfazerMeiaLargura); }
                catch (Exception e) { Console.Error.WriteLine($"  léxico: pulei {Path.GetFileName(pdf)}: {e.Message}"); }
                Progresso("léxico", Interlocked.Increment(ref feitos), pdfs.Count);
                return coletor;
            },
            parciais.Add);
        Console.Error.WriteLine();

        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in parciais)
            foreach (var (p, n) in c.Contagem)
                total[p] = total.GetValueOrDefault(p) + n;
        Lexico.Gravar(Path.Combine(cfg.Acervo, Lexico.Arquivo), total);
        return Lexico.DeContagem(total);
    }

    public static int Paralelismo => Math.Max(1, Environment.ProcessorCount / 2);

    public static void Progresso(string etapa, int feito, int total)
    {
        if (Console.IsErrorRedirected && feito != total && feito % 25 != 0) return;
        Console.Error.Write($"\r  {etapa}: {feito}/{total}   ");
    }

    /// <summary>
    /// Migração: importa a árvore <c>&lt;origem&gt;/&lt;ano&gt;/*.pdf</c> do acervo bruto. O ano vem da
    /// pasta; o resto, do nome do arquivo. A procedência (TSV com URL pública) é opcional.
    /// </summary>
    public static int ImportarArvore(Configuracao cfg, string origem, string idColecao, string? procedencia, int? limite, bool refazerLexico)
    {
        var repo = PrepararAcervo(cfg);
        var colecao = repo.Colecao(idColecao);
        var pdfs = Directory.EnumerateDirectories(origem)
            .Where(d => AnoDePasta().IsMatch(Path.GetFileName(d)))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.pdf"))
            .Order(StringComparer.Ordinal)
            .Take(limite ?? int.MaxValue)
            .ToList();
        // Arquivo com extensão .pdf que não é PDF (o de 1975, n.º 2, são 1.053 bytes zerados no
        // acervo bruto): não é erro do importador, é um desfalque a ser resolvido no original.
        var corrompidos = pdfs.Where(p => !EhPdf(p)).ToList();
        pdfs = pdfs.Except(corrompidos).ToList();
        Console.Error.WriteLine($"{pdfs.Count} PDFs em {origem}");
        foreach (var c in corrompidos) Console.WriteLine($"  IGNORADO (não é PDF — arquivo corrompido no acervo bruto): {c}");

        var lexico = refazerLexico ? null : CarregarLexico(cfg);
        if (lexico is null)
        {
            Console.Error.WriteLine("Montando o léxico do acervo (primeira importação)…");
            lexico = MontarLexico(cfg, pdfs, colecao);
            Console.Error.WriteLine($"  {lexico.Tamanho:N0} palavras");
        }

        var urls = LerProcedencia(procedencia);
        if (procedencia is not null) Console.Error.WriteLine($"procedência: {urls.Count} endereços públicos em {Path.GetFullPath(procedencia)}");
        var importador = new Importador(repo, new Extrator(new LeitorPoppler(cfg.Poppler), lexico));
        var resultados = new ConcurrentBag<(string Pdf, ResultadoDaImportacao? R, string? Erro)>();
        var relogio = Stopwatch.StartNew();
        var feitos = 0;
        Parallel.ForEach(pdfs, new ParallelOptions { MaxDegreeOfParallelism = Paralelismo }, pdf =>
        {
            var ano = Path.GetFileName(Path.GetDirectoryName(pdf))!;
            var campos = colecao.SugerirCampos(pdf);
            campos["ano"] = ano;
            var relativo = $"{ano}/{Path.GetFileName(pdf)}";
            try
            {
                var r = importador.Importar(pdf, colecao, campos, "migração", Path.GetFullPath(pdf), urls.GetValueOrDefault(relativo));
                resultados.Add((pdf, r, null));
            }
            catch (Exception e) { resultados.Add((pdf, null, e.Message)); }
            Progresso("importação", Interlocked.Increment(ref feitos), pdfs.Count);
        });
        Console.Error.WriteLine();

        var ok = resultados.Where(r => r.R is not null).Select(r => r.R!).ToList();
        var erros = resultados.Where(r => r.Erro is not null).ToList();
        var paginas = ok.Sum(r => r.Extracao.Paginas);
        var conferidas = ok.Sum(r => r.Extracao.PaginasConferidas);
        var semTexto = ok.Sum(r => r.Extracao.PaginasSemTexto);
        var divergentes = ok.Sum(r => r.Extracao.Divergencias.Count);
        Console.WriteLine($"{ok.Count} documentos ({ok.Count(r => r.Novo)} novos) · {paginas:N0} páginas · {conferidas:N0} conferidas · {divergentes} divergentes · {semTexto} sem texto · {erros.Count} erros · {relogio.Elapsed:mm\\:ss}");
        foreach (var (pdf, _, erro) in erros) Console.WriteLine($"  ERRO {pdf}: {erro}");
        foreach (var r in ok.Where(r => r.Extracao.Divergencias.Count > 0).OrderByDescending(r => r.Extracao.Divergencias.Count).Take(15))
            Console.WriteLine($"  divergente: {r.Arquivos.Id} — p. {string.Join(", ", r.Extracao.Divergencias.Select(d => d.Pagina))}");
        return erros.Count == 0 ? 0 : 2;
    }

    public static Dictionary<string, string> LerProcedencia(string? tsv)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tsv is null) return d;
        if (!File.Exists(tsv)) throw new FileNotFoundException($"arquivo de procedência não encontrado: {Path.GetFullPath(tsv)}");
        foreach (var linha in File.ReadLines(tsv).Skip(1))
        {
            var c = linha.Split('\t');
            if (c.Length >= 2) d[c[0]] = c[1];
        }
        return d;
    }

    public static bool EhPdf(string arquivo)
    {
        Span<byte> inicio = stackalloc byte[1024];
        using var f = File.OpenRead(arquivo);
        var n = f.Read(inicio);
        return System.Text.Encoding.ASCII.GetString(inicio[..n]).Contains("%PDF-", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex AnoDePasta();
}
