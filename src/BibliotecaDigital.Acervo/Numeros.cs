using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BibliotecaDigital.Acervo;

/// <summary>Números como os periódicos escrevem: tomo em romano ou arábico, edição "11-12" ou "06 a 10".</summary>
public static class Numeros
{
    /// <summary>"XXIX" e "29" são o mesmo grupo.</summary>
    public static int? Valor(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        texto = texto.Trim().ToUpperInvariant();
        if (int.TryParse(texto, out var n)) return n;
        var valores = new Dictionary<char, int> { ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000 };
        if (!texto.All(valores.ContainsKey)) return null;
        var total = 0;
        for (var i = 0; i < texto.Length; i++)
        {
            var v = valores[texto[i]];
            total += i + 1 < texto.Length && valores[texto[i + 1]] > v ? -v : v;
        }
        return total;
    }

    /// <summary>Números cobertos ("01-02" → 1, 2; "06 a 10" → 6..10; "011-12" → 11, 12).</summary>
    public static HashSet<int> Cobertos(string? numero)
    {
        var n = Regex.Matches(numero ?? "", @"\d+").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        if (n.Count == 2 && n[1] > n[0] && n[1] - n[0] <= 12) return Enumerable.Range(n[0], n[1] - n[0] + 1).ToHashSet();
        return n.ToHashSet();
    }

    public static string Romano(int valor)
    {
        var (valores, simbolos) = (new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 },
                                   new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" });
        var sb = new StringBuilder();
        for (var i = 0; i < valores.Length; i++)
            while (valor >= valores[i]) { sb.Append(simbolos[i]); valor -= valores[i]; }
        return sb.ToString();
    }

    /// <summary>Escreve um número no mesmo estilo de um exemplo (romano se o exemplo é romano).</summary>
    public static string ComoExemplo(int valor, string? exemplo) =>
        exemplo is { Length: > 0 } e && !e.Trim().All(char.IsDigit) ? Romano(valor) : valor.ToString(CultureInfo.InvariantCulture);
}
