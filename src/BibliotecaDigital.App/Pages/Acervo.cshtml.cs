using Microsoft.AspNetCore.Mvc;

namespace BibliotecaDigital.App.Pages;

public sealed class AcervoModel(Servicos servicos) : PaginaBase(servicos)
{
    [BindProperty(SupportsGet = true)] public string? Colecao { get; set; }
    [BindProperty(SupportsGet = true)] public string? Decada { get; set; }

    public IReadOnlyList<Navegacao.ResumoDaColecao> Colecoes { get; private set; } = [];
    public Navegacao.ResumoDaColecao? Atual { get; private set; }
    public IReadOnlyList<Navegacao.Decada> Decadas { get; private set; } = [];
    public Navegacao.Decada? Aberta { get; private set; }

    public void OnGet()
    {
        Colecoes = Navegacao.Resumo(S);
        Atual = Colecoes.FirstOrDefault(c => c.Colecao.Id == Colecao) ?? Colecoes.FirstOrDefault(c => c.Documentos > 0) ?? Colecoes.FirstOrDefault();
        if (Atual is null) return;
        Decadas = Navegacao.Arvore(S, Atual.Colecao.Id);
        Aberta = Decadas.FirstOrDefault(d => d.Nome == Decada) ?? Decadas.FirstOrDefault();
    }

    public static string Periodo(Navegacao.ResumoDaColecao r) =>
        r.Primeiro is { } a && r.Ultimo is { } b ? (a == b ? $"{a}" : $"{a}–{b}") : "sem data";
}
