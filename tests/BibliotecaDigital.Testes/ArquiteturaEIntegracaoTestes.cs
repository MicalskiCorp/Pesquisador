using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Busca;
using BibliotecaDigital.Extracao;
using Xunit.Abstractions;

namespace BibliotecaDigital.Testes;

internal static class Caminhos
{
    public static string Raiz { get; } = Achar();

    private static string Achar()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "configurar.ps1"))) return d.FullName;
        throw new InvalidOperationException("raiz do projeto (configurar.ps1) não encontrada");
    }

    public static string Config(string arquivo) => Path.Combine(Raiz, "config", arquivo);

    /// <summary>Arquivo de apoio dos testes, ao lado deles (<c>tests/BibliotecaDigital.Testes/</c>).</summary>
    public static string Teste(string arquivo) => Path.Combine(Raiz, "tests", "BibliotecaDigital.Testes", arquivo);
}

/// <summary>
/// O produto não tem IA dentro — decisão de projeto, não preferência. Este teste falha o build
/// se qualquer pacote de LLM, ML, embeddings ou busca vetorial entrar no backend ou na tela.
/// </summary>
public class ArquiteturaTestes
{
    // OCR também fica de fora (tesseract, paddle, easyocr): os motores atuais são redes neurais. O
    // A Biblioteca Digital lê a camada de texto que já vem no PDF; PDF sem texto é recusado na inclusão.
    private static readonly Regex Proibidos = new(
        @"(?i)(anthropic|openai|azure\.ai|semantic.?kernel|langchain|llama|ollama|onnx|ml\.net|microsoft\.ml|tensorflow|torch|" +
        @"huggingface|transformers|embedding|vector|pgvector|qdrant|pinecone|weaviate|chroma|gemini|mistral|cohere|@xenova|tiktoken|" +
        @"tesseract|paddle|easyocr|accord\.|numsharp|keras|sentencepiece|tokenizers)");

    private static IEnumerable<string> ArquivosDoProduto(params string[] extensoes) =>
        Directory.EnumerateFiles(Path.Combine(Caminhos.Raiz, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => extensoes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"));

    [Fact]
    public void Nenhuma_dependencia_de_IA_no_backend()
    {
        var pacotes = Directory.EnumerateFiles(Path.Combine(Caminhos.Raiz, "src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Descendants("PackageReference").Select(p => (string?)p.Attribute("Include") ?? ""))
            .ToList();
        Assert.NotEmpty(pacotes);
        Assert.DoesNotContain(pacotes, p => Proibidos.IsMatch(p));
    }

    /// <summary>
    /// Também as dependências das dependências: um pacote inocente pode trazer um de IA por baixo.
    /// Lê a lista completa que o restore resolveu (<c>obj/project.assets.json</c>) de cada projeto do produto.
    /// </summary>
    [Fact]
    public void Nenhuma_dependencia_de_IA_nem_indireta()
    {
        var arquivos = Directory.EnumerateFiles(Path.Combine(Caminhos.Raiz, "src"), "project.assets.json", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(arquivos);
        var bibliotecas = arquivos
            .SelectMany(f => System.Text.Json.JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("libraries").EnumerateObject().Select(l => l.Name))
            .Distinct()
            .ToList();
        var achadas = bibliotecas.Where(b => Proibidos.IsMatch(b)).ToList();
        Assert.True(achadas.Count == 0, "dependência proibida: " + string.Join(", ", achadas));
    }

    /// <summary>
    /// O produto não fala com a internet: nenhum serviço externo (de IA ou qualquer outro) pode ser
    /// chamado. Cliente de rede só no lançador, que pergunta ao próprio servidor em 127.0.0.1; todo
    /// endereço escrito no código é desta máquina.
    /// </summary>
    [Fact]
    public void O_produto_nao_chama_nenhum_servico_de_fora()
    {
        var rede = new Regex(@"\b(HttpClient|WebClient|WebRequest|HttpWebRequest|Socket|TcpClient|UdpClient|WebSocket|ClientWebSocket|Dns\.|SmtpClient|XMLHttpRequest|EventSource|navigator\.sendBeacon)\b");
        var comRede = ArquivosDoProduto(".cs", ".cshtml", ".js")
            .Where(f => rede.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(["Lancador.cs"], comRede);
        Assert.DoesNotMatch(new Regex(@"https?://(?!127\.0\.0\.1)"), File.ReadAllText(Path.Combine(Caminhos.Raiz, "src", "BibliotecaDigital.App", "Lancador.cs")));

        // Endereço literal só desta máquina (localhost, 127.0.0.1, 0.0.0.0, *) ou montado com o nome dela.
        // "$schema" de JSON é só para o editor completar o arquivo; o programa nunca o acessa.
        var externo = new Regex(@"(?i)(https?|wss?)://(?!localhost\b|127\.0\.0\.1\b|0\.0\.0\.0\b|\*|\{|<)");
        var achados = ArquivosDoProduto(".cs", ".cshtml", ".js", ".json")
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i, l)))
            .Where(x => externo.IsMatch(x.l) && !x.l.TrimStart().StartsWith("//") && !x.l.TrimStart().StartsWith("\"$schema\""))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.True(achados.Count == 0, "endereço externo em: " + string.Join(", ", achados));
    }

    /// <summary>
    /// Os únicos programas que a Biblioteca Digital executa: o Poppler (ler e desenhar PDF), ele mesmo (o
    /// lançador sobe o servidor), o navegador (abrir a tela) e sc.exe/netsh (instalar o serviço).
    /// </summary>
    [Fact]
    public void Programas_externos_so_os_conhecidos()
    {
        var processo = new Regex(@"\b(Process\.Start|ProcessStartInfo)\b");
        var arquivos = ArquivosDoProduto(".cs").Where(f => processo.IsMatch(File.ReadAllText(f))).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["Cli.cs", "Lancador.cs", "LeitorPoppler.cs"], arquivos);

        var cli = File.ReadAllText(Path.Combine(Caminhos.Raiz, "src", "BibliotecaDigital.App", "Cli.cs"));
        var programas = Regex.Matches(cli, @"\(""([^""]+)"",\s*\$?""").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(cli, @"Rodar\(""([^""]+)""").Select(m => m.Groups[1].Value))
            .Distinct().Order().ToList();
        Assert.Equal(["netsh", "sc.exe"], programas);

        var poppler = File.ReadAllText(Path.Combine(Caminhos.Raiz, "src", "BibliotecaDigital.Extracao", "LeitorPoppler.cs"));
        var nomes = Regex.Matches(poppler, @"""(pdf\w+)""").Select(m => m.Groups[1].Value).Distinct().Order().ToList();
        Assert.Equal(["pdfinfo", "pdftoppm", "pdftotext"], nomes);
    }

    /// <summary>
    /// A tela não carrega nada de fora — nem script, nem estilo, nem fonte. A máquina do Arquivo
    /// pode estar sem internet, e a Biblioteca Digital tem de abrir igual.
    /// </summary>
    [Fact]
    public void A_tela_nao_carrega_nada_de_fora()
    {
        var app = Path.Combine(Caminhos.Raiz, "src", "BibliotecaDigital.App");
        var arquivos = Directory.EnumerateFiles(Path.Combine(app, "Pages"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(app, "wwwroot"), "*.*").Where(f => f.EndsWith(".css") || f.EndsWith(".js")))
            .ToList();
        Assert.NotEmpty(arquivos);
        var externo = new Regex(@"(?i)(<script[^>]+src\s*=\s*[""']?(https?:)?//|<link[^>]+href\s*=\s*[""']?(https?:)?//|@import\s+url\(\s*[""']?(https?:)?//|url\(\s*[""']?(https?:)?//|fetch\(\s*[""']https?:)");
        var achados = arquivos.Where(f => externo.IsMatch(File.ReadAllText(f))).Select(Path.GetFileName).ToList();
        Assert.True(achados.Count == 0, "recurso externo em: " + string.Join(", ", achados));
    }
}

/// <summary>
/// Casos de conferência que dependem do acervo real: trechos, páginas e documentos que só existem
/// lá. Ficam no próprio acervo (<c>acervo/testes/*.json</c>), e não neste repositório, que é público
/// e serve a qualquer instituição. Sem o arquivo, o teste registra que não rodou.
/// </summary>
internal static class CasosDoAcervo
{
    public static string Acervo =>
        Environment.GetEnvironmentVariable("BIBLIOTECA_ACERVO") is { Length: > 0 } a ? a : Path.Combine(Caminhos.Raiz, "acervo");

    private static readonly JsonSerializerOptions Opcoes = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static T? Ler<T>(string arquivo, ITestOutputHelper saida)
    {
        var caminho = Path.Combine(Acervo, "testes", arquivo);
        if (File.Exists(caminho)) return JsonSerializer.Deserialize<T>(File.ReadAllText(caminho), Opcoes);
        saida.WriteLine($"não rodou: {caminho} não existe (os casos ficam no acervo da instituição)");
        return default;
    }
}

/// <summary>Um PDF do acervo e o que a extração tem de dar nele (<c>acervo/testes/bancada.json</c>).</summary>
public sealed record CasoDaBancada(string Nome, string Pdf, string Colecao, int? Paginas, IReadOnlyList<PaginaEsperada> Esperado);

public sealed record PaginaEsperada(
    int Pagina, string? Impressa, int? Colunas,
    IReadOnlyList<string>? Contem, IReadOnlyList<string>? NaoContem,
    IReadOnlyList<IReadOnlyList<string>>? Antes, IReadOnlyList<ParagrafoEsperado>? Paragrafos);

public sealed record ParagrafoEsperado(int Coluna, string ComecaCom);

/// <summary>
/// Bancada do extrator sobre PDFs reais do acervo: cada caso diz o que a extração tem de dar
/// numa página (impressa, colunas, trechos, ordem de leitura) e exige 100% das páginas conferidas.
/// Roda quando o acervo traz <c>testes/bancada.json</c> e o Poppler está na máquina.
/// </summary>
public class BancadaDoExtratorTestes(ITestOutputHelper saida)
{
    [Fact]
    public void Casos_do_acervo_saem_como_esperado_e_conferidos()
    {
        if (CasosDoAcervo.Ler<List<CasoDaBancada>>("bancada.json", saida) is not { } casos) return;
        Extrator extrator;
        IReadOnlyList<Colecao> colecoes;
        try
        {
            extrator = new Extrator(new LeitorPoppler(), Lexico.Carregar(Path.Combine(CasosDoAcervo.Acervo, Lexico.Arquivo)));
            colecoes = Colecoes.Ler(Path.Combine(CasosDoAcervo.Acervo, RepositorioDoAcervo.ArquivoDeColecoes));
        }
        catch (InvalidOperationException e) { saida.WriteLine("não rodou: " + e.Message); return; }

        var falhas = new List<string>();
        foreach (var caso in casos)
        {
            var colecao = colecoes.Single(c => c.Id == caso.Colecao);
            var pdf = Path.Combine(CasosDoAcervo.Acervo, caso.Pdf);
            var ficha = new Ficha
            {
                Id = "bancada", Colecao = colecao.Id, Titulo = "bancada", Campos = colecao.SugerirCampos(pdf),
                Pdf = new DadosDoPdf { Arquivo = Path.GetFileName(pdf), Bytes = 0, Sha256 = "", Paginas = 0 },
            };
            var r = extrator.Extrair(pdf, colecao, ficha);
            void Falha(string o) => falhas.Add($"{caso.Nome}: {o}");

            if (caso.Paginas is { } n && r.Paginas != n) Falha($"{r.Paginas} páginas, esperado {n}");
            if (r.PaginasConferidas != r.Paginas) Falha($"{r.PaginasConferidas} de {r.Paginas} páginas conferidas");
            foreach (var e in caso.Esperado)
            {
                var p = r.Documento.Paginas[e.Pagina - 1];
                var corpo = p.Corpo;
                if (e.Impressa is { } imp && p.Impressa != imp) Falha($"p. {e.Pagina}: impressa {p.Impressa}, esperado {imp}");
                if (e.Colunas is { } col && p.Colunas != col) Falha($"p. {e.Pagina}: {p.Colunas} colunas, esperado {col}");
                foreach (var s in e.Contem ?? []) if (!corpo.Contains(s, StringComparison.Ordinal)) Falha($"p. {e.Pagina}: falta \"{s}\"");
                foreach (var s in e.NaoContem ?? []) if (corpo.Contains(s, StringComparison.Ordinal)) Falha($"p. {e.Pagina}: não devia ter \"{s}\"");
                foreach (var par in e.Antes ?? [])
                {
                    int a = corpo.IndexOf(par[0], StringComparison.Ordinal), b = corpo.IndexOf(par[1], StringComparison.Ordinal);
                    if (a < 0 || b < 0 || a > b) Falha($"p. {e.Pagina}: \"{par[0]}\" devia vir antes de \"{par[1]}\"");
                }
                foreach (var pe in e.Paragrafos ?? [])
                    if (!p.Paragrafos.Any(x => x.Coluna == pe.Coluna && x.Texto.StartsWith(pe.ComecaCom, StringComparison.Ordinal)))
                        Falha($"p. {e.Pagina}: nenhum parágrafo da coluna {pe.Coluna} começa com \"{pe.ComecaCom}\"");
            }
            saida.WriteLine($"{caso.Nome}: {r.PaginasConferidas}/{r.Paginas} conferidas");
        }
        Assert.True(falhas.Count == 0, string.Join("\n", falhas));
    }
}

/// <summary>Uma consulta com resposta conhecida, conferida no PDF (<c>acervo/testes/gabarito.json</c>).</summary>
public sealed record ConsultaDoGabarito(string Q, Modo Modo, string Documento, int Pagina);

public sealed record Gabarito(IReadOnlyList<ConsultaDoGabarito> Consultas, IReadOnlyList<string>? Desempenho);

/// <summary>
/// Gabarito de busca: consultas com resposta conhecida. Roda quando o índice
/// (<c>dados/busca.db</c>) existe e o acervo traz <c>testes/gabarito.json</c>.
/// </summary>
public class GabaritoDeBuscaTestes(ITestOutputHelper saida)
{
    private (MotorDeBusca, Gabarito)? Preparar()
    {
        var banco = Path.Combine(Caminhos.Raiz, "dados", "busca.db");
        if (!File.Exists(banco)) { saida.WriteLine("não rodou: índice ausente — rode `biblioteca reindexar`"); return null; }
        return CasosDoAcervo.Ler<Gabarito>("gabarito.json", saida) is { } g ? (new MotorDeBusca(banco), g) : null;
    }

    [Fact]
    public void Consulta_conhecida_chega_na_pagina_certa()
    {
        if (Preparar() is not var (motor, gabarito)) return;
        var falhas = new List<string>();
        foreach (var c in gabarito.Consultas)
        {
            var r = motor.Buscar(new Pedido(c.Q, c.Modo, PorPagina: 200));
            var doc = r.Itens.FirstOrDefault(d => d.Id.EndsWith(c.Documento, StringComparison.Ordinal));
            if (doc is null) falhas.Add($"'{c.Q}' ({c.Modo}) não trouxe {c.Documento}. Veio: {string.Join(", ", r.Itens.Take(5).Select(d => d.Id))}");
            else if (!doc.PaginasComOcorrencia.Contains(c.Pagina)) falhas.Add($"'{c.Q}' ({c.Modo}) trouxe {c.Documento}, mas não na p. {c.Pagina}");
        }
        saida.WriteLine($"{gabarito.Consultas.Count - falhas.Count} de {gabarito.Consultas.Count} consultas certas");
        Assert.True(falhas.Count == 0, string.Join("\n", falhas));
    }

    /// <summary>
    /// Desempenho medido à parte e com o índice aquecido: tempo dentro do teste de resultado
    /// falhava quando a bancada do extrator disputava CPU na mesma rodada.
    /// </summary>
    [Fact]
    public void Busca_responde_em_menos_de_meio_segundo_no_percentil_95()
    {
        if (Preparar() is not var (motor, gabarito)) return;
        if (gabarito.Desempenho is not { Count: > 0 } consultas) { saida.WriteLine("não rodou: gabarito sem consultas de desempenho"); return; }
        var pedidos = consultas.SelectMany(q => new[] { Modo.Exato, Modo.Variacoes, Modo.Tolerante }.Select(m => new Pedido(q, m))).ToList();
        // Uma rodada inteira de aquecimento: a primeira leitura do índice depois de gravado vem
        // do disco (e passa pelo antivírus); o que interessa ao usuário é o regime estável.
        foreach (var p in pedidos) motor.Buscar(p);
        var tempos = pedidos.Select(p => motor.Buscar(p).Milissegundos).Order().ToList();
        var p95 = tempos[Math.Max(0, (int)(tempos.Count * 0.95) - 1)];
        saida.WriteLine($"p95 = {p95:0} ms, máximo = {tempos[^1]:0} ms em {tempos.Count} buscas");
        Assert.True(p95 < 500, $"p95 = {p95:0} ms");
    }
}
