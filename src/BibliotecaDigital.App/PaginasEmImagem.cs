using BibliotecaDigital.Busca;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.App;

/// <summary>Um retângulo marcado sobre a página, em frações da largura e da altura (0 a 1).</summary>
public sealed record CaixaRealcada(double X, double Y, double L, double A);

/// <summary>
/// As páginas do PDF como imagem, geradas sob demanda pelo Poppler embutido e guardadas num
/// cache em <c>dados/paginas</c> — nunca no acervo: é derivado e se apaga sem perda. O cache tem
/// teto; passou dele, saem as imagens usadas há mais tempo.
///
/// Existe porque o visualizador de PDF do navegador é uma caixa fechada: a página não sabe onde ele
/// está rolado. Com o PDF desenhado em imagens, as duas colunas (PDF e texto) rolam juntas.
/// </summary>
public sealed class PaginasEmImagem(Configuracao cfg, Func<LeitorPoppler> poppler)
{
    /// <summary>Larguras geradas: leitura normal, zoom e zoom grande. Pedido no meio sobe para a próxima.</summary>
    public static readonly int[] Larguras = [900, 1400, 2000];
    private const long Teto = 2L * 1024 * 1024 * 1024;

    private readonly SemaphoreSlim _vagas = new(Math.Max(2, Environment.ProcessorCount / 2));
    private int _desdeAUltimaPoda;

    private string Pasta => Path.Combine(cfg.Dados, "paginas");

    public async Task<string> Obter(string pdf, string sha256, int pagina, int largura)
    {
        largura = Larguras.FirstOrDefault(l => l >= largura, Larguras[^1]);
        var arquivo = Path.Combine(Pasta, sha256[..2], $"{sha256[..16]}-{pagina}-{largura}.jpg");
        if (File.Exists(arquivo))
        {
            try { File.SetLastAccessTimeUtc(arquivo, DateTime.UtcNow); } catch (IOException) { }
            return arquivo;
        }
        await _vagas.WaitAsync();
        try
        {
            if (!File.Exists(arquivo)) await Task.Run(() => poppler().Renderizar(pdf, pagina, largura, arquivo));
        }
        finally { _vagas.Release(); }
        if (Interlocked.Increment(ref _desdeAUltimaPoda) % 200 == 0) _ = Task.Run(Podar);
        return arquivo;
    }

    /// <summary>Mantém o cache abaixo do teto, apagando as imagens usadas há mais tempo.</summary>
    public void Podar()
    {
        if (!Directory.Exists(Pasta)) return;
        var arquivos = new DirectoryInfo(Pasta).EnumerateFiles("*.jpg", SearchOption.AllDirectories)
            .OrderBy(f => f.LastAccessTimeUtc).ToList();
        var total = arquivos.Sum(f => f.Length);
        foreach (var f in arquivos)
        {
            if (total <= Teto * 8 / 10) break;
            try { total -= f.Length; f.Delete(); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Onde, na página, estão as palavras que casam com a busca — pelas caixas das palavras que
    /// o Poppler lê do PDF. Vale em qualquer navegador (o "#search=" só funcionava no Firefox).
    /// </summary>
    public IReadOnlyList<CaixaRealcada> Realces(string pdf, int pagina, Func<string, bool> casa)
    {
        var layout = poppler().Ler(pdf, pagina, pagina).FirstOrDefault();
        if (layout is null || layout.Largura <= 0 || layout.Altura <= 0) return [];
        var lista = new List<CaixaRealcada>();
        foreach (var palavra in layout.Blocos.SelectMany(b => b.Linhas).SelectMany(l => l.Palavras))
        {
            if (!Tokens.Ler(palavra.Texto).Any(t => casa(t.Dobrado))) continue;
            var c = palavra.Caixa;
            lista.Add(new CaixaRealcada(c.X0 / layout.Largura, c.Y0 / layout.Altura, c.Largura / layout.Largura, c.Altura / layout.Altura));
        }
        return lista;
    }
}
