using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Busca;

namespace BibliotecaDigital.App;

/// <summary>
/// Monta o HTML de um parágrafo: termos da busca marcados e errata mostrada sobre o literal.
/// Todo texto passa por codificação — o OCR traz "&lt;", "&amp;" e afins.
/// </summary>
public static class Realce
{
    public static IHtmlContent Paragrafo(string texto, Func<string, bool>? casa, IEnumerable<Errata> errata)
    {
        var sb = new StringBuilder();
        var resto = texto;
        foreach (var e in errata)
        {
            var i = resto.IndexOf(e.Literal, StringComparison.Ordinal);
            if (i < 0) continue;
            Marcar(sb, resto[..i], casa);
            sb.Append("<span class=\"corrigido\" title=\"No PDF: “")
              .Append(WebUtility.HtmlEncode(e.Literal)).Append("” · correção de ")
              .Append(WebUtility.HtmlEncode(e.Por ?? "—")).Append("\">")
              .Append(WebUtility.HtmlEncode(e.Leitura)).Append("</span>");
            resto = resto[(i + e.Literal.Length)..];
        }
        Marcar(sb, resto, casa);
        return new HtmlString(sb.ToString());
    }

    public static IHtmlContent Trecho(Trecho trecho)
    {
        var sb = new StringBuilder();
        foreach (var s in trecho.Segmentos)
        {
            if (s.Destaque) sb.Append("<mark>").Append(WebUtility.HtmlEncode(s.Texto)).Append("</mark>");
            else sb.Append(WebUtility.HtmlEncode(s.Texto));
        }
        return new HtmlString(sb.ToString());
    }

    private static void Marcar(StringBuilder sb, string texto, Func<string, bool>? casa)
    {
        if (casa is null) { sb.Append(WebUtility.HtmlEncode(texto)); return; }
        var cursor = 0;
        foreach (var t in Tokens.Ler(texto))
        {
            if (!casa(t.Dobrado)) continue;
            sb.Append(WebUtility.HtmlEncode(texto[cursor..t.Inicio]))
              .Append("<mark>").Append(WebUtility.HtmlEncode(texto[t.Inicio..t.Fim])).Append("</mark>");
            cursor = t.Fim;
        }
        sb.Append(WebUtility.HtmlEncode(texto[cursor..]));
    }

    public static string Modo(Modo m) => m switch { Busca.Modo.Variacoes => "variacoes", Busca.Modo.Tolerante => "tolerante", _ => "exato" };

    public static Modo Modo(string? m) => m switch { "variacoes" => Busca.Modo.Variacoes, "tolerante" => Busca.Modo.Tolerante, _ => Busca.Modo.Exato };
}
