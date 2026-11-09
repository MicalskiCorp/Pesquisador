using System.Text.RegularExpressions;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Extracao;

/// <summary>
/// Descobre o número impresso de cada página do PDF a partir das linhas de margem.
///
/// Um número só é aceito quando um vizinho confirma a sequência: a p. 6 do PDF diz "4" e a
/// p. 7 diz "5". Isso descarta o ano no rodapé ("2015") e o dígito que o OCR estropiou. As
/// lacunas entre duas páginas confirmadas são preenchidas por interpolação — nunca além delas
/// (capa e anúncios do fim ficam sem número, como no original).
///
/// Digitalização em página dupla (duas impressas por página do PDF) aparece como passo 2:
/// a p. 20 do PDF é "36–37".
/// </summary>
public static partial class PaginacaoImpressa
{
    private const int Maximo = 1499; // acima disso é ano, não página

    public static IReadOnlyList<(string? Impressa, OrigemImpressa? Origem)> Calcular(
        IReadOnlyList<IReadOnlyList<string>> margensPorPagina,
        IReadOnlyDictionary<int, string>? ajustes = null)
    {
        var n = margensPorPagina.Count;
        var candidatos = margensPorPagina.Select(Candidatos).ToList();

        // Passo da paginação (1 ou 2), pelo que mais pares de vizinhos confirmam.
        int Votos(int passo)
        {
            var v = 0;
            for (var i = 0; i < n; i++)
                for (var d = 1; d <= 3 && i + d < n; d++)
                    if (candidatos[i].Any(a => candidatos[i + d].Contains(a + passo * d))) v++;
            return v;
        }
        var passoDoc = Votos(2) > Votos(1) ? 2 : 1;

        var confirmado = new int?[n];
        for (var i = 0; i < n; i++)
        {
            int? melhor = null;
            foreach (var a in candidatos[i])
            {
                var ok = false;
                for (var d = -3; d <= 3 && !ok; d++)
                {
                    var j = i + d;
                    if (d == 0 || j < 0 || j >= n) continue;
                    ok = candidatos[j].Contains(a + passoDoc * d);
                }
                if (ok && (melhor is null || a < melhor)) melhor = a;
            }
            confirmado[i] = melhor;
        }

        var resultado = new (string?, OrigemImpressa?)[n];
        string Rotulo(int v) => passoDoc == 2 ? $"{v}-{v + 1}" : v.ToString();

        int? anterior = null;
        for (var i = 0; i < n; i++)
        {
            if (confirmado[i] is { } v)
            {
                resultado[i] = (Rotulo(v), OrigemImpressa.Detectada);
                anterior = i;
                continue;
            }
            if (anterior is not { } a) continue;
            var proximo = Enumerable.Range(i + 1, n - i - 1).FirstOrDefault(j => confirmado[j] is not null, -1);
            if (proximo < 0) continue;
            var va = confirmado[a]!.Value;
            var vp = confirmado[proximo]!.Value;
            if (vp - va == passoDoc * (proximo - a))
                resultado[i] = (Rotulo(va + passoDoc * (i - a)), OrigemImpressa.Interpolada);
        }

        if (ajustes is not null)
            foreach (var (pagina, valor) in ajustes)
                if (pagina >= 1 && pagina <= n)
                    resultado[pagina - 1] = string.IsNullOrWhiteSpace(valor) ? (null, OrigemImpressa.Ajustada) : (valor.Trim(), OrigemImpressa.Ajustada);

        return resultado;
    }

    /// <summary>Números plausíveis de página numa linha de margem.</summary>
    internal static List<int> Candidatos(IReadOnlyList<string> margens)
    {
        var lista = new List<int>();
        foreach (var m in margens)
        {
            var t = m.Trim();
            foreach (var r in new[] { SoNumero(), NoInicio(), NoFim() })
            {
                var x = r.Match(t);
                if (x.Success && int.TryParse(x.Groups[1].Value, out var v) && v is >= 1 and <= Maximo)
                    lista.Add(v);
            }
        }
        return lista.Distinct().ToList();
    }

    [GeneratedRegex(@"^[^\p{L}\d]{0,4}(\d{1,4})[^\p{L}\d]{0,4}$")]
    private static partial Regex SoNumero();

    [GeneratedRegex(@"^(\d{1,4})\s+\p{L}")]
    private static partial Regex NoInicio();

    [GeneratedRegex(@"\p{L}.*\s(\d{1,4})$")]
    private static partial Regex NoFim();
}
