using System.Text;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Busca;

/// <summary>
/// A mesma noção de "palavra" que o índice usa (FTS5 unicode61, sem diacríticos): sequência
/// de letras e dígitos, comparada sem acento e em minúsculas. Busca, destaque de trecho e
/// vocabulário têm de concordar sobre isso, senão o trecho destacado não é o que casou.
/// </summary>
public static class Tokens
{
    public readonly record struct Token(string Dobrado, int Inicio, int Fim);

    public static List<Token> Ler(string texto)
    {
        var lista = new List<Token>();
        var i = 0;
        while (i < texto.Length)
        {
            while (i < texto.Length && !char.IsLetterOrDigit(texto[i])) i++;
            var ini = i;
            while (i < texto.Length && (char.IsLetterOrDigit(texto[i]) || char.GetUnicodeCategory(texto[i]) == System.Globalization.UnicodeCategory.NonSpacingMark)) i++;
            if (i > ini) lista.Add(new Token(Texto.Dobrado(texto[ini..i]), ini, i));
        }
        return lista;
    }

    public static IEnumerable<string> Dobrados(string texto) => Ler(texto).Select(t => t.Dobrado);
}

/// <summary>
/// Radical leve do português, para o modo "variações": junta plural e as formas de gênero e
/// grau mais regulares (colonos → colono, colônias → colonia, imigrantes → imigrante,
/// professora → professor, casinha → casinho). É conservador de propósito: não junta
/// "colono" com "colonização" — para isso há o prefixo (coloni*), que o biblioteca controla.
/// Regra fixa, sem dicionário e sem modelo: a mesma palavra dá sempre o mesmo radical.
/// </summary>
public static class RadicalPortugues
{
    private static readonly (string Fim, string Troca, int Minimo)[] Plurais =
    [
        ("oes", "ao", 4), ("aes", "ao", 4), ("ais", "al", 4), ("eis", "el", 4), ("ois", "ol", 4),
        ("ns", "m", 4), ("res", "r", 5), ("zes", "z", 5), ("ses", "s", 5), ("les", "l", 5),
    ];

    private static readonly (string Fim, string Troca, int Minimo)[] Femininos =
    [
        ("inha", "inho", 6), ("eira", "eiro", 6), ("ora", "or", 6), ("osa", "oso", 6), ("ica", "ico", 6),
        ("ada", "ado", 5), ("ida", "ido", 5), ("iva", "ivo", 5), ("ona", "ao", 6),
    ];

    public static string Radical(string dobrado)
    {
        var p = dobrado;
        if (p.Length < 4 || p.Any(char.IsDigit)) return p;

        var trocou = false;
        foreach (var (fim, troca, minimo) in Plurais)
            if (p.Length >= minimo && p.EndsWith(fim, StringComparison.Ordinal))
            { p = p[..^fim.Length] + troca; trocou = true; break; }
        if (!trocou && p.Length >= 4 && p[^1] == 's' && "aeiou".Contains(p[^2]))
            p = p[..^1];
        // "padres" virou "padr" pela regra do plural; o singular "padre" tem de chegar lá também.
        if (p.Length >= 4 && p[^1] == 'e' && "rzsl".Contains(p[^2]))
            p = p[..^1];

        foreach (var (fim, troca, minimo) in Femininos)
            if (p.Length >= minimo && p.EndsWith(fim, StringComparison.Ordinal))
            { p = p[..^fim.Length] + troca; break; }
        return p;
    }

    public static string Radicais(string texto) => string.Join(' ', Tokens.Dobrados(texto).Select(Radical));
}

/// <summary>
/// Distância de edição com custo menor para as trocas que o OCR do acervo comete de fato
/// (medidas na validação: ç→q e ã→á em 2015; m→rn; l, i, 1 e I; o e 0; e e c). É o que deixa
/// "edificacao" achar "edificaqao" sem achar qualquer palavra parecida.
/// </summary>
public static class DistanciaOcr
{
    private static readonly HashSet<(char, char)> Confusoes = Pares(
        "cq", "ce", "li", "l1", "i1", "il", "o0", "oc", "nu", "nh", "hb", "rn", "ft", "tl", "sz", "uv", "ao", "ea", "ij");

    private static HashSet<(char, char)> Pares(params string[] pares)
    {
        var h = new HashSet<(char, char)>();
        foreach (var p in pares) { h.Add((p[0], p[1])); h.Add((p[1], p[0])); }
        return h;
    }

    public static double Calcular(string a, string b, double teto = 3)
    {
        if (Math.Abs(a.Length - b.Length) > teto) return teto + 1;
        var d = new double[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var menor = double.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var custo = a[i - 1] == b[j - 1] ? 0 : Confusoes.Contains((a[i - 1], b[j - 1])) ? 0.5 : 1;
                var v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + custo);
                // "m" lido como "rn" e vice-versa: duas letras por uma, custo de meia troca.
                if (i >= 2 && a[i - 2] == 'r' && a[i - 1] == 'n' && b[j - 1] == 'm') v = Math.Min(v, d[i - 2, j - 1] + 0.5);
                if (j >= 2 && b[j - 2] == 'r' && b[j - 1] == 'n' && a[i - 1] == 'm') v = Math.Min(v, d[i - 1, j - 2] + 0.5);
                if (i >= 2 && j >= 2 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
                menor = Math.Min(menor, v);
            }
            if (menor > teto) return teto + 1;
        }
        return d[a.Length, b.Length];
    }

    /// <summary>Quanto erro aceitar para uma palavra deste tamanho.</summary>
    public static double Tolerancia(int tamanho) => tamanho <= 4 ? 0.5 : tamanho <= 7 ? 1 : tamanho <= 11 ? 2 : 2.5;
}
