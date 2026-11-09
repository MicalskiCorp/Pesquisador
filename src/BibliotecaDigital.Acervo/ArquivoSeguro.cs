using System.Security.Cryptography;
using System.Text;

namespace BibliotecaDigital.Acervo;

/// <summary>
/// Escrita em disco que nunca deixa arquivo pela metade: grava ao lado e troca de uma vez.
/// O acervo é permanente; uma queda de energia no meio de uma gravação não pode corrompê-lo.
/// </summary>
public static class ArquivoSeguro
{
    private static readonly UTF8Encoding Utf8SemBom = new(false);

    public static void GravarTexto(string caminho, string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminho))!);
        var temporario = caminho + ".gravando";
        File.WriteAllText(temporario, conteudo, Utf8SemBom);
        File.Move(temporario, caminho, overwrite: true);
    }

    public static void Copiar(string origem, string destino)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destino))!);
        var temporario = destino + ".gravando";
        File.Copy(origem, temporario, overwrite: true);
        File.Move(temporario, destino, overwrite: true);
    }

    public static string Sha256(string caminho)
    {
        using var f = File.OpenRead(caminho);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }
}
