using System.Text.Json;

namespace BibliotecaDigital.Acervo;

/// <summary>O PDF original: identidade e procedência.</summary>
public sealed record DadosDoPdf
{
    public required string Arquivo { get; init; }
    public required long Bytes { get; init; }
    public required string Sha256 { get; init; }
    public required int Paginas { get; init; }
    /// <summary>Programa que gerou a camada de OCR, segundo os metadados do próprio PDF.</summary>
    public string? Produtor { get; init; }
    /// <summary>De onde o arquivo veio (caminho no acervo bruto, ou "envio pela tela").</summary>
    public string? Origem { get; init; }
    /// <summary>Endereço público do mesmo arquivo, quando existe (um repositório on-line da instituição, por exemplo).</summary>
    public string? UrlPublica { get; init; }
}

/// <summary>Artigo catalogado: título, autor e páginas impressas.</summary>
public sealed record Artigo
{
    public required string Titulo { get; init; }
    public string? Autor { get; init; }
    public string? Secao { get; init; }
    /// <summary>Faixa de páginas IMPRESSAS, como o catálogo dos editores registra ("2-3").</summary>
    public string? Paginas { get; init; }
    public string? Fonte { get; init; }
}

/// <summary>Correção humana de um trecho de OCR. Nunca altera o texto literal.</summary>
public sealed record Errata
{
    public required string Id { get; init; }
    public required int Pagina { get; init; }
    public required string Literal { get; init; }
    public required string Leitura { get; init; }
    public string? Por { get; init; }
    public DateTimeOffset Em { get; init; }
}

public sealed record EventoDaFicha(DateTimeOffset Em, string Por, string Acao, string? Detalhe = null);

/// <summary>
/// A camada humana de um documento: tudo o que uma pessoa informou ou corrigiu. É o único
/// arquivo do documento editado pela tela; o <c>.md</c> é gerado a partir do PDF e dela.
/// </summary>
public sealed record Ficha
{
    // Nome antigo do projeto, mantido: o identificador está gravado em cada ficha do acervo.
    public const string Versao = "pesquisador/ficha/1";

    public string Formato { get; init; } = Versao;
    public required string Id { get; init; }
    public required string Colecao { get; init; }
    public required string Titulo { get; init; }
    public Dictionary<string, string> Campos { get; init; } = new();
    public required DadosDoPdf Pdf { get; init; }

    /// <summary>Página impressa ajustada à mão, por página do PDF. Vence a detecção.</summary>
    public Dictionary<int, string> PaginasImpressas { get; init; } = new();

    public List<Artigo> Artigos { get; init; } = [];
    public List<Errata> Errata { get; init; } = [];
    public List<EventoDaFicha> Historico { get; init; } = [];

    public static Ficha Ler(string arquivo) =>
        JsonSerializer.Deserialize<Ficha>(File.ReadAllText(arquivo), Colecoes.Json)
        ?? throw new InvalidDataException($"Ficha vazia: {arquivo}");

    public void Gravar(string arquivo) =>
        ArquivoSeguro.GravarTexto(arquivo, JsonSerializer.Serialize(this, Colecoes.Json) + "\n");
}
