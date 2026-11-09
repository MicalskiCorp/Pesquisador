using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Busca;

namespace BibliotecaDigital.App.Pages;

public sealed class IndexModel(Servicos servicos) : PaginaBase(servicos)
{
    public const int PorPagina = 20;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Modo { get; set; }
    /// <summary>Coleção escolhida; vazio busca em todo o acervo.</summary>
    [BindProperty(SupportsGet = true)] public string? Colecao { get; set; }
    [BindProperty(SupportsGet = true)] public int? De { get; set; }
    [BindProperty(SupportsGet = true)] public int? Ate { get; set; }
    [BindProperty(SupportsGet = true)] public string? Autor { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    /// <summary>Grafias que estavam na lista de variantes (todas) e as que ficaram marcadas.</summary>
    [BindProperty(SupportsGet = true)] public List<string> Todas { get; set; } = [];
    [BindProperty(SupportsGet = true)] public List<string> Manter { get; set; } = [];
    [BindProperty(SupportsGet = true)] public string? Excluir { get; set; }

    public ResultadoDaBusca? Resultado { get; private set; }
    public IReadOnlyList<Navegacao.ResumoDaColecao> Colecoes { get; private set; } = [];
    public string NomeDaColecao(string id) => Colecoes.FirstOrDefault(c => c.Colecao.Id == id)?.Colecao.Nome ?? id;
    public IReadOnlyList<string> Excluidas { get; private set; } = [];

    public Modo ModoAtual => Realce.Modo(Modo);
    public int TotalDePaginas => Resultado is null ? 0 : (Resultado.Documentos + PorPagina - 1) / PorPagina;

    public void OnGet()
    {
        Excluidas = Todas.Count > 0
            ? Todas.Except(Manter).ToList()
            : (Excluir ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Colecoes = Navegacao.Resumo(S);
        if (Colecao is { Length: > 0 } && Colecoes.All(c => c.Colecao.Id != Colecao)) Colecao = null;
        if (string.IsNullOrWhiteSpace(Q)) return;
        Resultado = S.Motor.Buscar(new Pedido(Q, ModoAtual, Colecao: Colecao, AnoDe: De, AnoAte: Ate, Autor: Autor,
            VariantesExcluidas: Excluidas.ToList(), Pagina: Math.Max(1, Pagina), PorPagina: PorPagina));
    }

    /// <summary>O endereço desta busca, mudando só o que se pede.</summary>
    public string Endereco(string? modo = null, int? pagina = null)
    {
        var p = new List<string> { "q=" + Uri.EscapeDataString(Q ?? "") };
        var m = modo ?? Modo;
        if (!string.IsNullOrEmpty(m) && m != "exato") p.Add("modo=" + m);
        if (!string.IsNullOrEmpty(Colecao)) p.Add("colecao=" + Uri.EscapeDataString(Colecao));
        if (De is { } de) p.Add("de=" + de);
        if (Ate is { } ate) p.Add("ate=" + ate);
        if (!string.IsNullOrWhiteSpace(Autor)) p.Add("autor=" + Uri.EscapeDataString(Autor));
        if (modo is null && Excluidas.Count > 0) p.Add("excluir=" + Uri.EscapeDataString(string.Join(',', Excluidas)));
        if (pagina is { } n && n > 1) p.Add("pagina=" + n);
        return "/?" + string.Join('&', p);
    }

    public static readonly (string Id, string Nome, string Ajuda)[] Modos =
    [
        ("exato", "Exato", "A palavra como foi digitada; acento e maiúscula não importam. \"Aspas\" para frase, coloni* para começo de palavra, -enchente para excluir."),
        ("variacoes", "Variações", "Também plural, feminino e diminutivo: colono acha colonos e colona; igreja acha igrejas."),
        ("tolerante", "Tolerante a OCR", "Também as grafias que a leitura automática errou e que existem no acervo (edificação ↔ edificaqáo). Você vê quais entraram e desmarca as que não servem."),
    ];
}
