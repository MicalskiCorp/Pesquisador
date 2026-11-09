namespace BibliotecaDigital.App;

/// <summary>
/// Onde ficam as coisas e em que porta a Biblioteca Digital atende. Vem do <c>appsettings.json</c> ao
/// lado do executável (seção <c>Biblioteca</c>) ou das variáveis <c>BIBLIOTECA_ACERVO</c>,
/// <c>BIBLIOTECA_DADOS</c>, <c>BIBLIOTECA_POPPLER</c> e <c>BIBLIOTECA_PORTA</c>, que vencem.
/// </summary>
public sealed record Configuracao(string Acervo, string Dados, string? Poppler, string Config, int Porta)
{
    public const int PortaPadrao = 8080;

    public string Banco => Path.Combine(Dados, "busca.db");
    public string Temporarios => Path.Combine(Dados, "envios");
    public string Registro => Path.Combine(Dados, "biblioteca.log");
    public string EnderecoLocal => $"http://localhost:{Porta}/";

    public static Configuracao Carregar()
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        return De(cfg);
    }

    public static Configuracao De(IConfiguration? cfg = null)
    {
        string? Ler(string env, string chave) =>
            Environment.GetEnvironmentVariable(env) is { Length: > 0 } v ? v : cfg?[$"Biblioteca:{chave}"];

        var baseDir = AppContext.BaseDirectory;
        var raizDoProjeto = AcharRaizDoProjeto(baseDir);
        var acervo = Ler("BIBLIOTECA_ACERVO", "Acervo") ?? Path.Combine(raizDoProjeto, "acervo");
        var dados = Ler("BIBLIOTECA_DADOS", "Dados") ?? Path.Combine(raizDoProjeto, "dados");
        var config = Ler("BIBLIOTECA_CONFIG", "Config")
            ?? new[] { Path.Combine(baseDir, "config"), Path.Combine(raizDoProjeto, "config") }.FirstOrDefault(Directory.Exists)
            ?? Path.Combine(baseDir, "config");
        // O Poppler vai junto do executável, na pasta poppler/; só em desenvolvimento vem de fora.
        var poppler = Ler("BIBLIOTECA_POPPLER", "Poppler")
            ?? (Directory.Exists(Path.Combine(baseDir, "poppler")) ? Path.Combine(baseDir, "poppler") : null);
        var porta = int.TryParse(Ler("BIBLIOTECA_PORTA", "Porta"), out var p) && p is > 0 and < 65536 ? p : PortaPadrao;
        return new Configuracao(Absoluto(acervo), Absoluto(dados), poppler is null ? null : Absoluto(poppler), Absoluto(config), porta);

        // Caminho relativo é relativo ao executável, não à pasta de onde ele foi chamado — senão
        // o mesmo pacote funciona com duplo-clique e falha como serviço do Windows.
        string Absoluto(string c) => Path.IsPathRooted(c) ? c : Path.GetFullPath(Path.Combine(baseDir, c));
    }

    /// <summary>
    /// A raiz do repositório (onde está o <c>configurar.ps1</c>). O acervo mora nela, em
    /// <c>acervo/</c>: o executável publicado dentro do repositório já encontra o acervo.
    /// </summary>
    private static string AcharRaizDoProjeto(string inicio)
    {
        for (var d = new DirectoryInfo(inicio); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, MarcadorDaRaiz))) return d.FullName;
        return inicio;
    }

    public const string MarcadorDaRaiz = "configurar.ps1";

    /// <summary>
    /// O nome da instituição dona do acervo, para a tela. Mora no próprio acervo
    /// (<c>acervo/biblioteca.json</c>, chave <c>instituicao</c>), e não no código nem no
    /// <c>appsettings.json</c>: o código é o mesmo para qualquer instituição, e o
    /// <c>appsettings.json</c> é substituído a cada atualização do programa.
    /// </summary>
    public string Instituicao => _instituicao ??= LerInstituicao(Acervo) ?? "";

    private string? _instituicao;

    private static string? LerInstituicao(string acervo)
    {
        var arquivo = Path.Combine(acervo, "biblioteca.json");
        if (!File.Exists(arquivo)) return null;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(arquivo));
            return json.RootElement.TryGetProperty("instituicao", out var v) ? v.GetString()?.Trim() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
