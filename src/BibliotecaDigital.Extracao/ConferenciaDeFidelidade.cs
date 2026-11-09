using System.Text;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Extracao;

/// <summary>Resultado da conferência de uma página.</summary>
public sealed record ResultadoDaConferencia(
    Fidelidade Fidelidade,
    IReadOnlyDictionary<string, int> Faltando,
    IReadOnlyDictionary<string, int> Sobrando,
    int NaoLidos);

/// <summary>
/// A garantia de que o texto bate com o PDF.
///
/// <b>Fidelidade</b> — exata, caractere por caractere, espaço à parte. O que a página guardou
/// (margens + corpo + hífens que o montador declarou ter removido) tem exatamente os
/// caracteres da camada de texto do PDF. Nada se perde, nada se inventa, nenhuma letra é
/// trocada: só ordem, quebra de linha e espaço mudam.
///
/// <b>Não lidos</b> — indicador, não selo. Posições de glifo que o PdfPig vê na página menos
/// caracteres que o Poppler leu. Pequenas diferenças são normais (o Poppler funde glifo
/// duplicado com deslocamento de fração de letra, e ignora pilha de lixo de OCR); um número
/// alto põe a página na lista de revisão da auditoria.
/// </summary>
public static class ConferenciaDeFidelidade
{
    public static ResultadoDaConferencia Conferir(PaginaLayout layout, int posicoesNoPdf, PaginaMontada montada, bool desfazerMeiaLargura)
    {
        string Normalizar(string s) =>
            (desfazerMeiaLargura ? Texto.DesfazerMeiaLargura(s, out _) : s).Normalize(NormalizationForm.FormC);

        var palavras = layout.Blocos.SelectMany(b => b.Linhas).SelectMany(l => l.Palavras).Select(p => p.Texto).ToList();
        var lido = Texto.Multiconjunto(palavras.Select(Normalizar));
        var guardado = Texto.Multiconjunto(
            montada.Margens.Concat(montada.Paragrafos.Select(p => p.Texto)).Concat(montada.HifensRemovidos));

        // Antes de desfazer a meia-largura: é a contagem de glifos que o Poppler entregou.
        var caracteresLidos = Texto.Multiconjunto(palavras).Values.Sum();
        var naoLidos = Math.Max(0, posicoesNoPdf - caracteresLidos);

        var vazio = new Dictionary<string, int>();
        if (posicoesNoPdf == 0 && lido.Count == 0)
            return new(Fidelidade.SemTexto, vazio, vazio, 0);

        var faltando = Diferenca(lido, guardado);
        var sobrando = Diferenca(guardado, lido);
        var f = faltando.Count == 0 && sobrando.Count == 0 ? Fidelidade.Conferida : Fidelidade.Divergente;
        return new(f, faltando, sobrando, naoLidos);
    }

    private static Dictionary<string, int> Diferenca(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (k, v) in a)
        {
            var resto = v - b.GetValueOrDefault(k);
            if (resto > 0) d[k] = resto;
        }
        return d;
    }
}
