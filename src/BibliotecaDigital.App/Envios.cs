using System.Text.Json;
using System.Threading.Channels;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.App;

public enum EstadoDoEnvio { NaFila, Lendo, Pronto, Erro }

/// <summary>O que se sabe de um PDF enviado pela tela e ainda não incluído.</summary>
public sealed record Envio
{
    public required string Id { get; init; }
    public required string Lote { get; init; }
    public required string NomeDoArquivo { get; init; }
    public required string Colecao { get; init; }
    public required string Sha256 { get; init; }
    public EstadoDoEnvio Estado { get; init; } = EstadoDoEnvio.NaFila;
    public string? Erro { get; init; }
    public Dictionary<string, string> Campos { get; init; } = new();
    public int Paginas { get; init; }
    public int Conferidas { get; init; }
    public int SemTexto { get; init; }
    public int Divergentes { get; init; }
    public bool TemTexto { get; init; }
    public string? DuplicadoDe { get; init; }
}

/// <summary>Vários PDFs enviados juntos para uma coleção.</summary>
public sealed record Lote(string Id, string Colecao, IReadOnlyList<string> Envios, DateTimeOffset Criado);

/// <summary>
/// PDFs enviados para prévia, em <c>dados/envios/</c>: o PDF, a prévia do texto (no mesmo formato
/// do acervo) e os dados do envio. Nada disso é acervo até a confirmação; o que ninguém confirma em
/// um dia é apagado. A leitura (extração + conferência) roda numa fila, um PDF por vez, para um lote
/// grande não travar o servidor.
/// </summary>
public static class Envios
{
    public static string Pdf(Configuracao cfg, string id) => Caminho(cfg, id, ".pdf", existe: true);
    public static string Previa(Configuracao cfg, string id) => Caminho(cfg, id, ".md");
    private static string Dados(Configuracao cfg, string id) => Caminho(cfg, id, ".json");
    private static string ArquivoDoLote(Configuracao cfg, string id) => Caminho(cfg, "lote-" + id, ".json");

    public static Envio Ler(Configuracao cfg, string id)
    {
        var f = Dados(cfg, id);
        return File.Exists(f) ? JsonSerializer.Deserialize<Envio>(File.ReadAllText(f), Colecoes.Json)!
            : throw new KeyNotFoundException("O envio expirou. Envie o arquivo de novo.");
    }

    public static Lote LerLote(Configuracao cfg, string id)
    {
        var f = ArquivoDoLote(cfg, id);
        return File.Exists(f) ? JsonSerializer.Deserialize<Lote>(File.ReadAllText(f), Colecoes.Json)!
            : throw new KeyNotFoundException("O lote expirou. Envie os arquivos de novo.");
    }

    public static DocumentoTexto LerPrevia(Configuracao cfg, string id) =>
        FormatoMarkdown.Ler(File.ReadAllText(Previa(cfg, id)));

    private static void Gravar(Configuracao cfg, Envio e) =>
        ArquivoSeguro.GravarTexto(Dados(cfg, e.Id), JsonSerializer.Serialize(e, Colecoes.Json));

    /// <summary>Guarda os arquivos e põe cada um na fila de leitura. Devolve o lote.</summary>
    public static async Task<Lote> Receber(Servicos s, IReadOnlyList<IFormFile> arquivos, string idColecao)
    {
        var colecao = s.Repositorio.Colecao(idColecao);
        var cfg = s.Cfg;
        Directory.CreateDirectory(cfg.Temporarios);
        Limpar(cfg.Temporarios);
        var lote = Guid.NewGuid().ToString("N");
        var ids = new List<string>();
        foreach (var arquivo in arquivos.Where(a => a.Length > 0))
        {
            if (!arquivo.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"“{arquivo.FileName}” não é PDF. O acervo guarda o original com a camada de texto do OCR.");
            var id = Guid.NewGuid().ToString("N");
            var pdf = Path.Combine(cfg.Temporarios, id + ".pdf");
            await using (var f = File.Create(pdf)) await arquivo.CopyToAsync(f);
            var nome = NomeDeArquivo(arquivo.FileName);
            var envio = new Envio
            {
                Id = id, Lote = lote, NomeDoArquivo = nome, Colecao = colecao.Id,
                Sha256 = ArquivoSeguro.Sha256(pdf), Campos = colecao.SugerirCampos(nome),
            };
            if (!Operacoes.EhPdf(pdf)) envio = envio with { Estado = EstadoDoEnvio.Erro, Erro = "O arquivo não é um PDF válido." };
            Gravar(cfg, envio);
            ids.Add(id);
        }
        if (ids.Count == 0) throw new InvalidOperationException("Escolha ao menos um PDF.");
        var l = new Lote(lote, colecao.Id, ids, DateTimeOffset.Now);
        ArquivoSeguro.GravarTexto(ArquivoDoLote(cfg, lote), JsonSerializer.Serialize(l, Colecoes.Json));
        foreach (var id in ids) s.FilaDeLeitura.Enfileirar(id);
        return l;
    }

    /// <summary>Lê um envio da fila: extrai, confere e guarda a prévia.</summary>
    public static void Processar(Servicos s, string id)
    {
        var cfg = s.Cfg;
        var envio = Ler(cfg, id);
        if (envio.Estado is EstadoDoEnvio.Pronto or EstadoDoEnvio.Erro) return;
        Gravar(cfg, envio with { Estado = EstadoDoEnvio.Lendo });
        try
        {
            var colecao = s.Repositorio.Colecao(envio.Colecao);
            var campos = new Dictionary<string, string>(envio.Campos);
            var ficha = new Ficha
            {
                Id = "previa", Colecao = colecao.Id, Titulo = colecao.TituloDoDocumento(campos, envio.NomeDoArquivo), Campos = campos,
                Pdf = new DadosDoPdf { Arquivo = envio.NomeDoArquivo, Bytes = new FileInfo(Pdf(cfg, id)).Length, Sha256 = envio.Sha256, Paginas = 0 },
            };
            var r = s.Extrator.Extrair(Pdf(cfg, id), colecao, ficha);
            // Ano pela capa, se a coleção tem um campo de ano ainda vazio.
            if (colecao.Campos.FirstOrDefault(c => c.Tipo == TiposDeCampo.Ano) is { } campoAno
                && !campos.ContainsKey(campoAno.Chave) && SugerirAno(r.Documento) is { } ano)
                campos[campoAno.Chave] = ano;
            ArquivoSeguro.GravarTexto(Previa(cfg, id), FormatoMarkdown.Gravar(r.Documento));
            var duplicado = s.Repositorio.Documentos().FirstOrDefault(d => File.Exists(d.Ficha) && Ficha.Ler(d.Ficha).Pdf.Sha256 == envio.Sha256);
            Gravar(cfg, envio with
            {
                Estado = EstadoDoEnvio.Pronto, Campos = campos, Paginas = r.Paginas, Conferidas = r.PaginasConferidas,
                SemTexto = r.PaginasSemTexto, Divergentes = r.Divergencias.Count, TemTexto = r.TemTexto, DuplicadoDe = duplicado?.Id,
            });
        }
        catch (Exception e)
        {
            Gravar(cfg, envio with { Estado = EstadoDoEnvio.Erro, Erro = e.Message });
        }
    }

    /// <summary>Envios que ficaram na fila quando o servidor parou: voltam para a fila.</summary>
    public static IEnumerable<string> Pendentes(Configuracao cfg)
    {
        if (!Directory.Exists(cfg.Temporarios)) yield break;
        foreach (var f in Directory.EnumerateFiles(cfg.Temporarios, "*.json"))
        {
            var nome = Path.GetFileNameWithoutExtension(f);
            if (nome.StartsWith("lote-", StringComparison.Ordinal)) continue;
            Envio? e = null;
            try { e = JsonSerializer.Deserialize<Envio>(File.ReadAllText(f), Colecoes.Json); } catch (JsonException) { }
            if (e is { Estado: EstadoDoEnvio.NaFila or EstadoDoEnvio.Lendo }) yield return e.Id;
        }
    }

    public static Envio AtualizarCampos(Configuracao cfg, string id, Dictionary<string, string> campos, string nome)
    {
        var e = Ler(cfg, id) with { Campos = campos, NomeDoArquivo = NomeDeArquivo(nome) };
        Gravar(cfg, e);
        return e;
    }

    public static void Descartar(Configuracao cfg, string id)
    {
        foreach (var ext in new[] { ".pdf", ".md", ".json" })
        {
            var f = Caminho(cfg, id, ext);
            if (File.Exists(f)) File.Delete(f);
        }
    }

    public static void DescartarLote(Configuracao cfg, string lote)
    {
        foreach (var id in LerLote(cfg, lote).Envios) Descartar(cfg, id);
        File.Delete(ArquivoDoLote(cfg, lote));
    }

    /// <summary>
    /// O nome do arquivo raramente traz o ano; a capa traz ("Janeiro de 1958"). Sugere o ano mais
    /// citado nas três primeiras páginas. É sugestão: o curador confere.
    /// </summary>
    public static string? SugerirAno(DocumentoTexto doc)
    {
        var texto = string.Join('\n', doc.Paginas.Take(3).SelectMany(p => p.Margens.Concat(p.Paragrafos.Select(x => x.Texto))));
        return System.Text.RegularExpressions.Regex.Matches(texto, @"(?<!\d)(1[5-9]\d\d|20\d\d)(?!\d)")
            .Select(m => int.Parse(m.Value))
            .Where(a => a >= 1500 && a <= DateTime.Now.Year)
            .GroupBy(a => a).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
            .Select(g => g.Key.ToString()).FirstOrDefault();
    }

    /// <summary>Só o nome, terminado em .pdf: o caminho do arquivo é a coleção que decide.</summary>
    public static string NomeDeArquivo(string nome)
    {
        nome = Path.GetFileName(nome.Trim());
        foreach (var c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '-');
        return nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? nome : nome + ".pdf";
    }

    private static string Caminho(Configuracao cfg, string id, string extensao, bool existe = false)
    {
        var nucleo = id.StartsWith("lote-", StringComparison.Ordinal) ? id[5..] : id;
        if (nucleo.Length != 32 || !nucleo.All(char.IsAsciiHexDigitLower))
            throw new InvalidOperationException("Envio inválido.");
        var f = Path.Combine(cfg.Temporarios, id + extensao);
        return !existe || File.Exists(f) ? f : throw new KeyNotFoundException("O envio expirou. Envie o arquivo de novo.");
    }

    private static void Limpar(string pasta)
    {
        foreach (var f in Directory.EnumerateFiles(pasta))
            if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1))
                try { File.Delete(f); } catch (IOException) { }
    }
}

/// <summary>A fila de leitura dos envios: um PDF por vez, em segundo plano.</summary>
public sealed class FilaDeLeitura(Servicos servicos, ILogger<FilaDeLeitura> log) : BackgroundService
{
    private readonly Channel<string> _fila = Channel.CreateUnbounded<string>();

    public void Enfileirar(string envio) => _fila.Writer.TryWrite(envio);

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        foreach (var pendente in Envios.Pendentes(servicos.Cfg)) Enfileirar(pendente);
        await foreach (var id in _fila.Reader.ReadAllAsync(parar))
        {
            try { await Task.Run(() => Envios.Processar(servicos, id), parar); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Leitura do envio {Envio} falhou", id); }
        }
    }
}
