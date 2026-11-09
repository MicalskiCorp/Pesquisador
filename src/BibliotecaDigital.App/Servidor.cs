using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Busca;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.App;

/// <summary>Serviços compartilhados pelas páginas. As escritas no acervo passam uma de cada vez.</summary>
public sealed class Servicos(Configuracao cfg)
{
    public Configuracao Cfg { get; } = cfg;
    public RepositorioDoAcervo Repositorio { get; } = new(cfg.Acervo);
    public MotorDeBusca Motor { get; } = new(cfg.Banco);
    public Indexador Indexador => new(Repositorio);
    public SemaphoreSlim Escrita { get; } = new(1, 1);

    private LeitorPoppler? _poppler;
    public LeitorPoppler Poppler => _poppler ??= new LeitorPoppler(Cfg.Poppler);

    private Extrator? _extrator;
    public Extrator Extrator => _extrator ??= new Extrator(Poppler, Operacoes.CarregarLexico(Cfg));
    public Importador Importador => new(Repositorio, Extrator);

    private PaginasEmImagem? _paginas;
    public PaginasEmImagem Paginas => _paginas ??= new PaginasEmImagem(Cfg, () => Poppler);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<(double Largura, double Altura)>> _tamanhos = new();

    /// <summary>Tamanho de cada página do PDF (guardado por SHA-256: PDF substituído, tamanho novo).</summary>
    public IReadOnlyList<(double Largura, double Altura)> Tamanhos(ArquivosDoDocumento d, Ficha f) =>
        _tamanhos.GetOrAdd(f.Pdf.Sha256, _ => Poppler.Tamanhos(d.Pdf, f.Pdf.Paginas));

    /// <summary>A fila que lê os PDFs enviados pela curadoria, um por vez.</summary>
    public FilaDeLeitura FilaDeLeitura { get; set; } = null!;

    public ArquivosDoDocumento Documento(string id) =>
        Repositorio.Localizar(id) ?? throw new KeyNotFoundException($"Documento não encontrado: {id}");

    private (RelatorioDeAuditoria Relatorio, DateTime Quando)? _auditoria;
    private Task? _auditando;

    /// <summary>
    /// A última auditoria e quando foi feita. Auditar relê todos os textos (num acervo de ~600 documentos, ~15 s): roda em segundo
    /// plano ao subir o servidor e depois de cada alteração, e a página mostra o resultado guardado.
    /// </summary>
    public (RelatorioDeAuditoria Relatorio, DateTime Quando)? UltimaAuditoria => _auditoria;
    public bool Auditando => _auditando is { IsCompleted: false };

    public void AuditarEmSegundoPlano()
    {
        if (Auditando) return;
        _auditando = Task.Run(() =>
        {
            try { _auditoria = (Auditoria.Auditar(Repositorio, conferirHash: false), DateTime.Now); }
            catch (Exception) { /* registrado pela próxima auditoria pedida na tela */ }
        });
    }

    /// <summary>Executa uma escrita no acervo com exclusividade.</summary>
    public async Task<T> Escrever<T>(Func<T> acao)
    {
        await Escrita.WaitAsync();
        try { return acao(); }
        finally { Escrita.Release(); AuditarEmSegundoPlano(); }
    }
}

/// <summary>O servidor web: as páginas, o PDF original e o texto. Sem login — uso interno.</summary>
public static class Servidor
{
    public static int Executar(Configuracao cfg, string[] args)
    {
        Directory.CreateDirectory(cfg.Dados);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // As páginas e o CSS ficam ao lado do executável, rode-se de onde for (e como serviço).
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Host.UseWindowsService(o => o.ServiceName = "BibliotecaDigital");
        // Todas as interfaces: atende também a rede do Arquivo, não só esta máquina.
        if (!args.Any(a => a.StartsWith("--urls", StringComparison.Ordinal)))
            builder.WebHost.UseUrls($"http://*:{cfg.Porta}"); // IPv4 e IPv6
        // PDF de edição chega a 160 MB.
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1024L * 1024 * 1024);
        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 1024L * 1024 * 1024);
        builder.Logging.AddProvider(new RegistroEmArquivo(cfg.Registro));
        builder.Services.AddRazorPages();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        var servicos = new Servicos(cfg);
        builder.Services.AddSingleton(servicos);
        builder.Services.AddSingleton<FilaDeLeitura>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<FilaDeLeitura>());

        var app = builder.Build();
        servicos.FilaDeLeitura = app.Services.GetRequiredService<FilaDeLeitura>();
        app.UseExceptionHandler("/erro");
        app.UseStatusCodePagesWithReExecute("/erro", "?codigo={0}");
        app.UseStaticFiles();
        app.UseRouting();
        Mapear(app, servicos);
        app.MapRazorPages();

        app.Lifetime.ApplicationStarted.Register(servicos.AuditarEmSegundoPlano);
        app.Lifetime.ApplicationStarted.Register(() => AquecerIndice(cfg, app.Logger));
        app.Logger.LogInformation("Biblioteca Digital {Versao} em http://0.0.0.0:{Porta} · acervo {Acervo}", Extrator.Versao, cfg.Porta, cfg.Acervo);
        app.Run();
        return 0;
    }

    /// <summary>
    /// Lê o índice inteiro uma vez, em segundo plano, logo que o servidor sobe. Sem isso, cada
    /// busca nova ia ao disco buscar os trechos do índice que ainda não tinham sido lidos: 1 a 4 s
    /// por busca na primeira vez, 20 ms depois. Com o arquivo no cache do sistema, todas ficam rápidas.
    /// </summary>
    private static void AquecerIndice(Configuracao cfg, ILogger log) => Task.Run(() =>
    {
        if (!File.Exists(cfg.Banco)) return;
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var f = new FileStream(cfg.Banco, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
            var buffer = new byte[4 << 20];
            while (f.Read(buffer, 0, buffer.Length) > 0) { }
            log.LogInformation("Índice aquecido em {Segundos:0.0} s", relogio.Elapsed.TotalSeconds);
        }
        catch (IOException) { }
    });

    private static void Mapear(WebApplication app, Servicos s)
    {
        // O lançador pergunta aqui se a Biblioteca Digital já está no ar.
        app.MapGet("/estado", () => new
        {
            aplicativo = "biblioteca",
            extrator = Extrator.Versao,
            documentos = s.Repositorio.Documentos().Count(),
            indice = File.Exists(s.Cfg.Banco),
        });

        // Sem nome de arquivo: o navegador abre o PDF no visualizador dele, na página do #page=N.
        app.MapGet("/pdf", (string id, bool? baixar) =>
        {
            var d = s.Documento(id);
            return baixar == true
                ? Results.File(d.Pdf, "application/pdf", Path.GetFileName(d.Pdf), enableRangeProcessing: true)
                : Results.File(d.Pdf, "application/pdf", lastModified: File.GetLastWriteTimeUtc(d.Pdf), enableRangeProcessing: true);
        });

        app.MapGet("/texto", (string id) =>
        {
            var d = s.Documento(id);
            return Results.File(d.Texto, "text/markdown; charset=utf-8", Path.GetFileName(d.Texto));
        });

        app.MapGet("/curadoria/envio", (string envio) =>
            Results.File(Envios.Pdf(s.Cfg, envio), "application/pdf", enableRangeProcessing: true));

        // A página do PDF como imagem. O endereço leva o começo do SHA-256 (v=): o navegador pode
        // guardar para sempre, porque PDF substituído muda o endereço.
        app.MapGet("/pagina", async (HttpContext ctx, string id, int p, int? w) =>
        {
            var d = s.Documento(id);
            var f = Ficha.Ler(d.Ficha);
            if (p < 1 || p > Math.Max(1, f.Pdf.Paginas)) return Results.NotFound();
            var jpg = await s.Paginas.Obter(d.Pdf, f.Pdf.Sha256, p, w ?? 900);
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(jpg, "image/jpeg");
        });

        app.MapGet("/curadoria/envio/pagina", async (string envio, int p, int? w) =>
        {
            var e = Envios.Ler(s.Cfg, envio);
            if (p < 1 || p > Math.Max(1, e.Paginas)) return Results.NotFound();
            return Results.File(await s.Paginas.Obter(Envios.Pdf(s.Cfg, envio), e.Sha256, p, w ?? 900), "image/jpeg");
        });

        // Onde o termo buscado está na imagem da página.
        app.MapGet("/realces", (string id, int p, string q, string? modo) =>
        {
            var d = s.Documento(id);
            var casa = s.Motor.Realcador(q, Realce.Modo(modo));
            return s.Paginas.Realces(d.Pdf, p, casa);
        });

        // Destino recalculado a cada tecla no formulário de inclusão (a página funciona sem isto).
        app.MapPost("/curadoria/destino", (CamposDoEnvio p) =>
        {
            var colecao = s.Repositorio.Colecao(p.Colecao);
            string? erro = null;
            try { Importador.Validar(colecao, p.Campos); } catch (InvalidOperationException e) { erro = e.Message; }
            var destino = s.Repositorio.Destino(colecao, p.Campos, Envios.NomeDeArquivo(p.NomeDoArquivo));
            return new
            {
                caminho = "acervo/" + Path.GetRelativePath(s.Repositorio.Raiz, destino.Pdf).Replace('\\', '/'),
                titulo = colecao.TituloDoDocumento(p.Campos, p.NomeDoArquivo),
                existe = File.Exists(destino.Pdf),
                erro,
            };
        }).DisableAntiforgery();
    }
}

public sealed record CamposDoEnvio(string Colecao, Dictionary<string, string> Campos, string NomeDoArquivo);

/// <summary>
/// Registro em arquivo (<c>dados/biblioteca.log</c>). O servidor roda sem janela; sem isto,
/// um erro na máquina do Arquivo não deixaria rastro.
/// </summary>
public sealed class RegistroEmArquivo(string arquivo) : ILoggerProvider
{
    private readonly object _trava = new();

    public ILogger CreateLogger(string categoria) => new Registro(this, categoria);
    public void Dispose() { }

    private void Escrever(string linha)
    {
        lock (_trava)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
                if (File.Exists(arquivo) && new FileInfo(arquivo).Length > 5 * 1024 * 1024)
                    File.Move(arquivo, arquivo + ".anterior", overwrite: true);
                File.AppendAllText(arquivo, linha + Environment.NewLine);
            }
            catch (IOException) { }
        }
    }

    private sealed class Registro(RegistroEmArquivo dono, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel nivel) =>
            nivel >= LogLevel.Warning || (nivel >= LogLevel.Information && categoria.StartsWith("BibliotecaDigital", StringComparison.OrdinalIgnoreCase));

        public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? erro, Func<TState, Exception?, string> formatar)
        {
            if (!IsEnabled(nivel)) return;
            dono.Escrever($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {nivel,-11} {categoria}: {formatar(estado, erro)}{(erro is null ? "" : Environment.NewLine + erro)}");
        }
    }
}
