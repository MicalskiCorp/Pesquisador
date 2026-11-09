using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;

namespace BibliotecaDigital.Extracao;

/// <summary>
/// Lê a geometria do texto de um PDF com o <c>pdftotext -bbox-layout</c> do Poppler: palavras,
/// linhas e blocos com coordenadas, e os blocos já em ordem de leitura (colunas separadas).
/// Validado em PDFs reais do acervo: é o que separa as colunas que o pdfplumber intercalava.
/// </summary>
public sealed class LeitorPoppler
{
    private readonly string _pdftotext;

    public LeitorPoppler(string? pastaDoPoppler = null)
    {
        _pdftotext = Localizar(pastaDoPoppler);
    }

    public string Executavel => _pdftotext;

    /// <summary>
    /// Largura e altura de cada página, em pontos, já com o giro aplicado — numa chamada só do
    /// <c>pdfinfo</c>. A tela reserva o espaço de cada página antes de a imagem chegar, para a
    /// rolagem não pular enquanto carrega.
    /// </summary>
    public IReadOnlyList<(double Largura, double Altura)> Tamanhos(string pdf, int paginas)
    {
        var pdfinfo = Irmao("pdfinfo");
        var psi = new ProcessStartInfo(pdfinfo) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-f", "1", "-l", Math.Max(1, paginas).ToString(CultureInfo.InvariantCulture), pdf }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var saida = p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
        var tamanhos = new Dictionary<int, (double, double)>();
        var giros = new Dictionary<int, int>();
        foreach (var linha in saida.Split('\n'))
        {
            var t = System.Text.RegularExpressions.Regex.Match(linha, @"^Page\s+(\d+)\s+size:\s+([\d.]+)\s+x\s+([\d.]+)");
            if (t.Success)
                tamanhos[int.Parse(t.Groups[1].Value)] = (double.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture), double.Parse(t.Groups[3].Value, CultureInfo.InvariantCulture));
            var r = System.Text.RegularExpressions.Regex.Match(linha, @"^Page\s+(\d+)\s+rot:\s+(\d+)");
            if (r.Success) giros[int.Parse(r.Groups[1].Value)] = int.Parse(r.Groups[2].Value);
        }
        return Enumerable.Range(1, paginas).Select(n =>
        {
            var (w, h) = tamanhos.GetValueOrDefault(n, (595, 842));
            return giros.GetValueOrDefault(n) % 180 == 90 ? (h, w) : (w, h);
        }).ToList();
    }

    private string Irmao(string nome)
    {
        var dir = Path.GetDirectoryName(_pdftotext)!;
        var caminho = Path.Combine(dir, OperatingSystem.IsWindows() ? nome + ".exe" : nome);
        return File.Exists(caminho) ? caminho : (OperatingSystem.IsWindows() ? nome + ".exe" : nome);
    }

    /// <summary>
    /// Desenha uma página do PDF como JPEG, com a largura pedida em pixels, usando o
    /// <c>pdftoppm</c> que vem junto do <c>pdftotext</c>. É o que a tela mostra no lugar do
    /// visualizador do navegador — a única forma de vincular a rolagem do PDF à do texto.
    /// </summary>
    public void Renderizar(string pdf, int pagina, int largura, string destinoJpg)
    {
        var pdftoppm = Irmao("pdftoppm");
        Directory.CreateDirectory(Path.GetDirectoryName(destinoJpg)!);
        // Nome temporário único: duas pessoas pedindo a mesma página ao mesmo tempo não colidem.
        var raiz = destinoJpg[..^".jpg".Length] + "." + Guid.NewGuid().ToString("N")[..8] + ".gerando";
        var psi = new ProcessStartInfo(pdftoppm) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[]
                 {
                     "-f", pagina.ToString(CultureInfo.InvariantCulture), "-l", pagina.ToString(CultureInfo.InvariantCulture),
                     "-scale-to-x", largura.ToString(CultureInfo.InvariantCulture), "-scale-to-y", "-1",
                     "-jpeg", "-jpegopt", "quality=82,progressive=y", "-singlefile", pdf, raiz,
                 })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var erro = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            p.Kill(true);
            throw new TimeoutException($"pdftoppm excedeu 60 s na página {pagina} de {pdf}");
        }
        if (p.ExitCode != 0 || !File.Exists(raiz + ".jpg"))
            throw new InvalidOperationException($"pdftoppm falhou na página {pagina}: {erro.Result}");
        File.Move(raiz + ".jpg", destinoJpg, overwrite: true);
    }

    /// <summary>
    /// Acha o pdftotext do Poppler. Recusa o do xpdf (o do Git for Windows), que tem o mesmo
    /// nome mas não tem <c>-bbox-layout</c> — falhar cedo e claro é melhor que extrair errado.
    /// </summary>
    private static string Localizar(string? pasta)
    {
        var nome = OperatingSystem.IsWindows() ? "pdftotext.exe" : "pdftotext";
        var candidatos = new List<string>();
        if (!string.IsNullOrWhiteSpace(pasta)) candidatos.Add(Path.Combine(pasta, nome));
        if (Environment.GetEnvironmentVariable("BIBLIOTECA_POPPLER") is { Length: > 0 } env)
            candidatos.Add(Path.Combine(env, nome));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (dir.Length > 0) candidatos.Add(Path.Combine(dir, nome));

        foreach (var c in candidatos.Where(File.Exists))
            if (EhPoppler(c)) return c;

        throw new InvalidOperationException(
            "pdftotext do Poppler não encontrado. Instale o Poppler (no Linux: poppler-utils) e " +
            "aponte a pasta em BIBLIOTECA_POPPLER ou em Biblioteca:Poppler.");
    }

    private static bool EhPoppler(string executavel)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(executavel, "-v")
            {
                RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
            })!;
            var saida = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return saida.Contains("Poppler", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public IReadOnlyList<PaginaLayout> Ler(string pdf, int? primeira = null, int? ultima = null)
    {
        var psi = new ProcessStartInfo(_pdftotext)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-bbox-layout");
        psi.ArgumentList.Add("-enc");
        psi.ArgumentList.Add("UTF-8");
        if (primeira is { } f) { psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(f.ToString(CultureInfo.InvariantCulture)); }
        if (ultima is { } l) { psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(l.ToString(CultureInfo.InvariantCulture)); }
        psi.ArgumentList.Add(pdf);
        psi.ArgumentList.Add("-");

        using var processo = Process.Start(psi)!;
        var erro = processo.StandardError.ReadToEndAsync();
        string xhtml = processo.StandardOutput.ReadToEnd();
        if (!processo.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            processo.Kill(true);
            throw new TimeoutException($"pdftotext excedeu 5 minutos em {pdf}");
        }
        if (processo.ExitCode != 0)
            throw new InvalidOperationException($"pdftotext falhou ({processo.ExitCode}) em {pdf}: {erro.Result}");

        return Interpretar(xhtml, primeira ?? 1);
    }

    internal static IReadOnlyList<PaginaLayout> Interpretar(string xhtml, int primeira)
    {
        var paginas = new List<PaginaLayout>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var r = XmlReader.Create(new StringReader(xhtml), settings);

        double largura = 0, altura = 0;
        int fluxo = -1;
        List<Bloco>? blocos = null;
        List<Linha>? linhas = null;
        List<Palavra>? palavras = null;
        Caixa caixaBloco = default, caixaLinha = default;

        r.Read();
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "word")
            {
                var caixa = Ler(r);
                // Lê o conteúdo e já avança para o nó seguinte: não chamar Read() de novo.
                string texto;
                if (r.IsEmptyElement) { texto = ""; r.Read(); }
                else texto = r.ReadElementContentAsString();
                if (texto.Length > 0) palavras?.Add(new Palavra(texto, caixa));
                continue;
            }
            if (r.NodeType == XmlNodeType.Element)
            {
                switch (r.LocalName)
                {
                    case "page":
                        largura = D(r, "width"); altura = D(r, "height");
                        blocos = []; fluxo = -1;
                        if (r.IsEmptyElement) { paginas.Add(new PaginaLayout(primeira + paginas.Count, largura, altura, blocos)); blocos = null; }
                        break;
                    case "flow": fluxo++; break;
                    case "block": caixaBloco = Ler(r); linhas = []; break;
                    case "line": caixaLinha = Ler(r); palavras = []; break;
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                switch (r.LocalName)
                {
                    case "line":
                        if (palavras is { Count: > 0 }) linhas?.Add(new Linha(palavras, caixaLinha));
                        palavras = null; break;
                    case "block":
                        if (linhas is { Count: > 0 }) blocos?.Add(new Bloco(fluxo, linhas, caixaBloco));
                        linhas = null; break;
                    case "page":
                        // Página girada: o Poppler declara as dimensões sem o giro, mas entrega as
                        // palavras já giradas (x até 689 numa página de 612 de largura, em 2015).
                        var b = blocos ?? [];
                        var (w, h) = (largura, altura);
                        if (b.Count > 0 && b.Max(x => x.Caixa.X1) > w * 1.02 && b.Max(x => x.Caixa.Y1) <= w * 1.02)
                            (w, h) = (h, w);
                        paginas.Add(new PaginaLayout(primeira + paginas.Count, w, h, b));
                        blocos = null; break;
                }
            }
            r.Read();
        }
        return paginas;
    }

    private static Caixa Ler(XmlReader r) => new(D(r, "xMin"), D(r, "yMin"), D(r, "xMax"), D(r, "yMax"));

    private static double D(XmlReader r, string atributo) =>
        double.TryParse(r.GetAttribute(atributo), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
