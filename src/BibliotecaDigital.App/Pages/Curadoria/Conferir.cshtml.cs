using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.App.Pages.Curadoria;

public sealed class ConferirModel(Servicos servicos) : PaginaBase(servicos)
{
    public RelatorioDeAuditoria? Relatorio { get; private set; }
    public DateTime? Quando { get; private set; }
    public bool Auditando { get; private set; }
    public string? Mensagem { get; private set; }

    public void OnGet(string? msg)
    {
        Mensagem = msg;
        if (S.UltimaAuditoria is { } u) { Relatorio = u.Relatorio; Quando = u.Quando; }
        else S.AuditarEmSegundoPlano();
        Auditando = S.Auditando;
    }

    public IActionResult OnPostConferir()
    {
        S.AuditarEmSegundoPlano();
        return Redirect("/curadoria/conferir");
    }

    public async Task<IActionResult> OnPostReindexarAsync()
    {
        var r = await S.Escrever(() => S.Indexador.ReconstruirTudo(S.Cfg.Banco));
        var msg = $"Índice refeito: {r.Documentos} edições, {r.Paginas:N0} páginas, {r.Termos:N0} termos, em {r.Duracao.TotalSeconds:0} s."
                  + (r.Erros.Count > 0 ? $" {r.Erros.Count} erro(s): {string.Join("; ", r.Erros.Take(3))}" : "");
        return Redirect("/curadoria/conferir?msg=" + Uri.EscapeDataString(msg));
    }
}
