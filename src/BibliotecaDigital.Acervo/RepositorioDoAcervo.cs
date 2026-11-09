namespace BibliotecaDigital.Acervo;

/// <summary>Onde moram os três arquivos de um documento.</summary>
public sealed record ArquivosDoDocumento(string Id, string Colecao, string Pasta, string NomeBase)
{
    public string Pdf => Path.Combine(Pasta, NomeBase + ".pdf");
    public string Ficha => Path.Combine(Pasta, NomeBase + ".ficha.json");
    public string Texto => Path.Combine(Pasta, NomeBase + ".md");
}

/// <summary>
/// O acervo em disco: <c>colecoes.json</c> na raiz e, por coleção, a árvore de pastas com
/// PDF, ficha e texto lado a lado. Este é o dado verdadeiro; o banco de busca é derivado.
/// </summary>
public sealed class RepositorioDoAcervo
{
    public const string ArquivoDeColecoes = "colecoes.json";

    public RepositorioDoAcervo(string raiz)
    {
        Raiz = Path.GetFullPath(raiz);
    }

    public string Raiz { get; }

    public IReadOnlyList<Colecao> Colecoes()
    {
        var arquivo = Path.Combine(Raiz, ArquivoDeColecoes);
        return File.Exists(arquivo) ? Acervo.Colecoes.Ler(arquivo) : [];
    }

    public Colecao Colecao(string id) =>
        Colecoes().FirstOrDefault(c => c.Id == id)
        ?? throw new KeyNotFoundException($"Coleção '{id}' não existe em {ArquivoDeColecoes}.");

    /// <summary>Identificador estável: o caminho relativo, cada trecho convertido em slug.</summary>
    public static string IdPara(string colecao, string pastaRelativa, string nomeBase)
    {
        var partes = new List<string> { colecao };
        partes.AddRange(pastaRelativa.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries).Select(Texto.Slug));
        partes.Add(Texto.Slug(nomeBase));
        return string.Join('/', partes);
    }

    /// <summary>Onde um documento novo deve ser gravado, segundo a regra de pastas da coleção.</summary>
    public ArquivosDoDocumento Destino(Colecao colecao, IReadOnlyDictionary<string, string> campos, string nomeDoArquivo)
    {
        // Campo ainda vazio (ano não informado) deixa segmento vazio: "{decada}/{ano}" vira "/", e
        // um caminho que começa em "/" escaparia da pasta do acervo.
        var relativa = string.Join('/', colecao.PastaDoDocumento(campos)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s is not "." and not ".."));
        var nomeBase = Path.GetFileNameWithoutExtension(nomeDoArquivo).Trim();
        var pasta = Path.Combine(Raiz, colecao.Id, relativa);
        return new ArquivosDoDocumento(IdPara(colecao.Id, relativa, nomeBase), colecao.Id, pasta, nomeBase);
    }

    /// <summary>Todos os documentos com ficha, em ordem de caminho.</summary>
    public IEnumerable<ArquivosDoDocumento> Documentos()
    {
        if (!Directory.Exists(Raiz)) yield break;
        foreach (var ficha in Directory.EnumerateFiles(Raiz, "*.ficha.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            yield return DeFicha(ficha);
    }

    public ArquivosDoDocumento DeFicha(string caminhoDaFicha)
    {
        var pasta = Path.GetDirectoryName(caminhoDaFicha)!;
        var nomeBase = Path.GetFileName(caminhoDaFicha)[..^".ficha.json".Length];
        var relativa = Path.GetRelativePath(Raiz, pasta).Replace('\\', '/');
        var colecao = relativa.Split('/')[0];
        var dentro = relativa.Length > colecao.Length ? relativa[(colecao.Length + 1)..] : "";
        return new ArquivosDoDocumento(IdPara(colecao, dentro, nomeBase), colecao, pasta, nomeBase);
    }

    public ArquivosDoDocumento? Localizar(string id) =>
        Documentos().FirstOrDefault(d => d.Id == id);
}
