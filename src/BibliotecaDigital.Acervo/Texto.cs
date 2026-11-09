using System.Globalization;
using System.Text;

namespace BibliotecaDigital.Acervo;

/// <summary>
/// Utilidades de texto usadas em todo o projeto. Ficam num lugar só porque a busca, o
/// formato e a extração precisam concordar exatamente sobre o que é "o mesmo texto".
/// </summary>
public static class Texto
{
    /// <summary>Remove acentos e diacríticos, preservando o resto (ç vira c).</summary>
    public static string SemAcento(string s)
    {
        var decomposto = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Forma de comparação: sem acento e em minúsculas.</summary>
    public static string Dobrado(string s) => SemAcento(s).ToLowerInvariant();

    /// <summary>
    /// Identificador estável para URL: minúsculas, sem acento, só letras, dígitos e hífen.
    /// </summary>
    public static string Slug(string s)
    {
        var sb = new StringBuilder();
        var hifen = false;
        foreach (var c in Dobrado(s))
        {
            if (char.IsAsciiLetterOrDigit(c)) { sb.Append(c); hifen = false; }
            else if (!hifen && sb.Length > 0) { sb.Append('-'); hifen = true; }
        }
        return sb.ToString().TrimEnd('-');
    }

    /// <summary>
    /// Conserta a corrupção "meia-largura" dos PDFs do PDFCreator (1966–1998): o OCR gravou
    /// letras Latin-1 deslocadas em 0xFF00 (U+FF61 '｡' é 'a'). Subtrair 0xFF00 devolve a letra
    /// gravada. Não corrige OCR — decodifica um defeito de codificação.
    /// </summary>
    public static string DesfazerMeiaLargura(string s, out int convertidos)
    {
        convertidos = 0;
        StringBuilder? sb = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c >= '＠' && c <= '￿' && c != '�' && c != '﻿')
            {
                sb ??= new StringBuilder(s, 0, i, s.Length);
                var original = c - 0xFF00;
                // 0x80–0x9F é a faixa em que Windows-1252 difere de Latin-1 (– “ ” …).
                sb.Append(original switch
                {
                    0x7F => ' ',
                    >= 0x80 and <= 0x9F => Cp1252[original - 0x80],
                    _ => (char)original,
                });
                convertidos++;
            }
            else sb?.Append(c);
        }
        return sb?.ToString() ?? s;
    }

    private const string Cp1252 =
        "€ ‚ƒ„…†‡ˆ‰Š‹Œ Ž  ‘’“”•–—˜™š›œ žŸ";

    /// <summary>Contagem de caracteres não-brancos, a base da verificação de fidelidade.</summary>
    public static Dictionary<string, int> Multiconjunto(IEnumerable<string> partes)
    {
        var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var parte in partes)
        {
            var e = StringInfo.GetTextElementEnumerator(parte.Normalize(NormalizationForm.FormC));
            while (e.MoveNext())
            {
                var g = e.GetTextElement();
                if (string.IsNullOrWhiteSpace(g)) continue;
                contagem[g] = contagem.GetValueOrDefault(g) + 1;
            }
        }
        return contagem;
    }
}
