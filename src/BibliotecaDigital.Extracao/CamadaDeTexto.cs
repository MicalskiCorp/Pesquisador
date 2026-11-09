using UglyToad.PdfPig;

namespace BibliotecaDigital.Extracao;

/// <summary>Metadados do PDF e, por página, quantas posições de glifo com texto ele tem.</summary>
public sealed record CamadaDeTexto(int Paginas, string? Produtor, IReadOnlyList<int> PosicoesPorPagina);

/// <summary>
/// Lê o PDF com o PdfPig — uma biblioteca independente do Poppler, que monta o texto. Serve
/// de segunda opinião: quantos glifos com texto cada página tem. Se o Poppler leu bem menos
/// caracteres que isso, a página vai para revisão humana.
/// </summary>
public static class LeitorDaCamadaDeTexto
{

    public static CamadaDeTexto Ler(string pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var posicoes = new List<int>(doc.NumberOfPages);
        foreach (var p in doc.GetPages())
        {
            // O OCR empilha glifos no mesmo ponto onde não reconheceu nada (56 cópias de um,
            // ou cinco letras diferentes). Na página isso é um sinal só: conta-se a posição.
            posicoes.Add(p.Letters
                .Where(l => !string.IsNullOrWhiteSpace(l.Value))
                .Select(l => (Math.Round(l.BoundingBox.Left, 1), Math.Round(l.BoundingBox.Bottom, 1)))
                .Distinct()
                .Count());
        }

        var info = doc.Information;
        var produtor = new[] { info.Producer, info.Creator }
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
        return new CamadaDeTexto(doc.NumberOfPages, produtor, posicoes);
    }
}
