using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.App.Pages.Curadoria;

public sealed class DesfalquesModel(Servicos servicos) : PaginaBase(servicos)
{
    [BindProperty(SupportsGet = true)] public string? Colecao { get; set; }

    /// <summary>Só as coleções com numeração (periódicos) têm desfalques.</summary>
    public IReadOnlyList<Colecao> ComNumeracao { get; private set; } = [];
    public Colecao? Atual { get; private set; }
    public IReadOnlyList<Desfalque> Lista { get; private set; } = [];
    public int Documentos { get; private set; }

    public void OnGet()
    {
        ComNumeracao = S.Repositorio.Colecoes().Where(c => c.Numeracao is not null).ToList();
        Atual = ComNumeracao.FirstOrDefault(c => c.Id == Colecao) ?? ComNumeracao.FirstOrDefault();
        if (Atual is null) return;
        Lista = Desfalques.Calcular(S.Repositorio, Atual, DateTime.Now.Year);
        Documentos = S.Repositorio.Documentos().Count(d => d.Colecao == Atual.Id);
    }
}
