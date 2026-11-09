using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Busca;

namespace BibliotecaDigital.App.Pages;

public sealed class DocumentoModel(Servicos servicos) : PaginaBase(servicos)
{
    [BindProperty(SupportsGet = true)] public string Id { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Modo { get; set; }

    public Ficha Ficha { get; private set; } = null!;
    public Colecao Colecao { get; private set; } = null!;
    public DocumentoTexto? Texto { get; private set; }
    public IReadOnlyList<int> Ocorrencias { get; private set; } = [];
    public IReadOnlyList<(double Largura, double Altura)> Tamanhos { get; private set; } = [];
    /// <summary>Começo do SHA-256 do PDF: entra no endereço das imagens, que o navegador guarda para sempre.</summary>
    public string Versao => Ficha.Pdf.Sha256[..12];
    public Func<string, bool>? Casa { get; private set; }
    private IReadOnlyList<System.Text.RegularExpressions.Regex> _carimbos = [];

    public string UrlDoPdf => "/pdf?id=" + Uri.EscapeDataString(Id);
    public int? Ano => Colecao.Ano(Ficha.Campos);
    public string? Decada => Ano is { } a ? $"{a - a % 10}–{a - a % 10 + 9}" : null;

    public IActionResult OnGet()
    {
        var d = S.Repositorio.Localizar(Id);
        if (d is null) return NotFound();
        Ficha = Ficha.Ler(d.Ficha);
        Colecao = S.Repositorio.Colecao(Ficha.Colecao);
        Texto = System.IO.File.Exists(d.Texto) ? FormatoMarkdown.Ler(System.IO.File.ReadAllText(d.Texto)) : null;
        P = Math.Clamp(P, 1, Math.Max(1, Texto?.Paginas.Count ?? Ficha.Pdf.Paginas));
        Tamanhos = S.Tamanhos(d, Ficha);
        _carimbos = Colecao.Extracao.Margens.Select(m => new System.Text.RegularExpressions.Regex(m, System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var modo = Realce.Modo(Modo);
            Casa = S.Motor.Realcador(Q, modo);
            var r = S.Motor.Buscar(new Pedido(Q, modo, Documento: Id, PorPagina: 1));
            Ocorrencias = r.Itens.FirstOrDefault()?.PaginasComOcorrencia ?? [];
        }
        return Page();
    }

    /// <summary>Margens sem o carimbo de digitalização, que está em toda página e não diz nada.</summary>
    public IEnumerable<string> Margens(PaginaTexto p) => p.Margens.Where(m => !_carimbos.Any(r => r.IsMatch(m)));

    public string Imagem(int pagina, int largura) =>
        $"/pagina?id={Uri.EscapeDataString(Id)}&p={pagina}&w={largura}&v={Versao}";

    public string? EnderecoDosRealces => string.IsNullOrWhiteSpace(Q) ? null
        : $"/realces?id={Uri.EscapeDataString(Id)}&q={Uri.EscapeDataString(Q)}&modo={Modo ?? "exato"}";

    public static string NomeDaFidelidade(Fidelidade f) => f switch
    {
        Fidelidade.Conferida => "conferida",
        Fidelidade.Divergente => "divergente",
        _ => "sem texto",
    };

    public static string ExplicacaoDaFidelidade(Fidelidade f) => f switch
    {
        Fidelidade.Conferida => "Cada caractere da camada de texto do PDF está aqui, e nada além dele.",
        Fidelidade.Divergente => "A conferência achou diferença de caracteres entre o PDF e este texto. Confira na imagem.",
        _ => "O PDF não tem texto nesta página — veja a imagem.",
    };
}
