using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BibliotecaDigital.App.Pages;

/// <summary>
/// Base das páginas. Sem login, o histórico das fichas guarda o nome que a pessoa digita; ele fica
/// num cookie deste navegador para não ser pedido a cada alteração.
/// </summary>
public abstract class PaginaBase(Servicos servicos) : PageModel
{
    private const string CookieDoNome = "biblioteca-nome";

    protected Servicos S => servicos;

    public string Nome => Request.Cookies[CookieDoNome] ?? "";

    protected string LembrarNome(string? nome)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length > 0)
            Response.Cookies.Append(CookieDoNome, nome, new Microsoft.AspNetCore.Http.CookieOptions
            {
                Expires = DateTimeOffset.Now.AddYears(1), HttpOnly = true, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            });
        return nome.Length > 0 ? nome : "não informado";
    }

    public static string LinkDoDocumento(string id, int? pagina = null, string? q = null, string? modo = null)
    {
        var url = "/documento?id=" + Uri.EscapeDataString(id);
        if (pagina is { } p) url += "&p=" + p;
        if (!string.IsNullOrWhiteSpace(q)) url += "&q=" + Uri.EscapeDataString(q);
        if (!string.IsNullOrWhiteSpace(modo) && modo != "exato") url += "&modo=" + modo;
        return url;
    }
}
