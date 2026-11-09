namespace BibliotecaDigital.Acervo;

/// <summary>Resultado da conferência de uma página contra a camada de texto do PDF.</summary>
public enum Fidelidade
{
    /// <summary>Todo caractere da camada de texto está na página, e nada além dele.</summary>
    Conferida,
    /// <summary>Sobrou ou faltou caractere que as regras de extração não explicam.</summary>
    Divergente,
    /// <summary>O PDF não tem texto nesta página (capa ilustrada, foto, página em branco).</summary>
    SemTexto,
}

/// <summary>De onde veio o número de página impressa.</summary>
public enum OrigemImpressa { Detectada, Interpolada, Ajustada }

/// <summary>Um parágrafo e a coluna da página em que ele está (1 = primeira).</summary>
public sealed record Paragrafo(string Texto, int Coluna = 1);

/// <summary>
/// O texto de uma página do PDF, no molde permanente do acervo: número da página do PDF,
/// número impresso, as linhas de margem (cabeçalho, rodapé, carimbo) e o corpo em parágrafos.
/// </summary>
public sealed record PaginaTexto
{
    public required int Numero { get; init; }
    public string? Impressa { get; init; }
    public OrigemImpressa? OrigemImpressa { get; init; }
    public Fidelidade Fidelidade { get; init; } = Fidelidade.Conferida;
    public int Colunas { get; init; } = 1;
    public int HifensRemovidos { get; init; }
    public int MeiaLargura { get; init; }
    /// <summary>Glifos que o PDF tem e a leitura não trouxe (indicador para revisão, não selo).</summary>
    public int NaoLidos { get; init; }
    public IReadOnlyList<string> Margens { get; init; } = [];
    public IReadOnlyList<Paragrafo> Paragrafos { get; init; } = [];

    /// <summary>Corpo da página em texto corrido, um parágrafo por linha.</summary>
    public string Corpo => string.Join('\n', Paragrafos.Select(p => p.Texto));
}

/// <summary>O arquivo <c>.md</c> de um documento: cabeçalho e páginas.</summary>
public sealed record DocumentoTexto
{
    /// <summary>Pares do cabeçalho, na ordem em que são gravados.</summary>
    public required IReadOnlyList<KeyValuePair<string, string>> Cabecalho { get; init; }
    public required string Titulo { get; init; }
    public required IReadOnlyList<PaginaTexto> Paginas { get; init; }

    public string? Valor(string chave) =>
        Cabecalho.FirstOrDefault(kv => kv.Key == chave).Value;
}
