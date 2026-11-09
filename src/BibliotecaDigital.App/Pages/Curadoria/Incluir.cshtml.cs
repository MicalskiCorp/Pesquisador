using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.App.Pages.Curadoria;

/// <summary>Uma linha da conferência do lote: o que a pessoa ajustou num envio.</summary>
public sealed class LinhaDoLote
{
    public string Id { get; set; } = "";
    public bool Incluir { get; set; }
    public string Nome { get; set; } = "";
    public Dictionary<string, string> Campos { get; set; } = new();
}

public sealed record ResultadoDaInclusao(string Arquivo, string? Id, string? Erro);

/// <summary>
/// Inclusão no acervo, em três passos: escolher a coleção e os PDFs; esperar a leitura (cada PDF é
/// extraído e conferido numa fila); conferir os campos na tabela do lote e incluir.
/// </summary>
public sealed class IncluirModel(Servicos servicos) : PaginaBase(servicos)
{
    [BindProperty(SupportsGet = true)] public string? Colecao { get; set; }
    [BindProperty(SupportsGet = true)] public string? Lote { get; set; }
    [BindProperty(SupportsGet = true)] public string? Envio { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    [BindProperty] public List<LinhaDoLote> Linhas { get; set; } = [];
    [BindProperty] public Dictionary<string, string> Todos { get; set; } = new();
    [BindProperty] public string? Por { get; set; }

    public IReadOnlyList<Colecao> Colecoes { get; private set; } = [];
    public Colecao? ColecaoDoLote { get; private set; }
    public IReadOnlyList<Envio> Envios { get; private set; } = [];
    public Envio? Previa { get; private set; }
    public PaginaTexto? PaginaDaPrevia { get; private set; }
    public IReadOnlyList<ResultadoDaInclusao> Resultados { get; private set; } = [];
    public string? Erro { get; private set; }

    public bool Lendo => Envios.Any(e => e.Estado is EstadoDoEnvio.NaFila or EstadoDoEnvio.Lendo);

    public IActionResult OnGet()
    {
        Colecoes = S.Repositorio.Colecoes();
        try
        {
            if (Envio is not null) CarregarPrevia(Envio);
            else if (Lote is not null) CarregarLote(Lote);
        }
        catch (KeyNotFoundException e) { Erro = e.Message; Lote = null; Envio = null; }
        return Page();
    }

    public async Task<IActionResult> OnPostEnviarAsync(List<IFormFile> arquivos, string? por)
    {
        Colecoes = S.Repositorio.Colecoes();
        LembrarNome(por);
        if (string.IsNullOrEmpty(Colecao) || Colecoes.All(c => c.Id != Colecao)) { Erro = "Escolha a coleção."; return Page(); }
        try
        {
            var lote = await global::BibliotecaDigital.App.Envios.Receber(S, arquivos, Colecao);
            return Redirect("/curadoria/incluir?lote=" + lote.Id);
        }
        catch (InvalidOperationException e) { Erro = e.Message; return Page(); }
    }

    /// <summary>Copia o que foi preenchido em "todos" para todas as linhas do lote.</summary>
    public IActionResult OnPostAplicar()
    {
        if (Lote is null) return Redirect("/curadoria/incluir");
        Guardar();
        var preenchidos = Todos.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToList();
        foreach (var l in Linhas)
        {
            var e = global::BibliotecaDigital.App.Envios.Ler(S.Cfg, l.Id);
            var campos = new Dictionary<string, string>(e.Campos);
            foreach (var (k, v) in preenchidos) campos[k] = v.Trim();
            global::BibliotecaDigital.App.Envios.AtualizarCampos(S.Cfg, l.Id, campos, e.NomeDoArquivo);
        }
        return Redirect("/curadoria/incluir?lote=" + Lote);
    }

    public async Task<IActionResult> OnPostIncluirAsync()
    {
        Colecoes = S.Repositorio.Colecoes();
        if (Lote is null) return Redirect("/curadoria/incluir");
        Guardar();
        var por = LembrarNome(Por);
        var resultados = new List<ResultadoDaInclusao>();
        foreach (var l in Linhas.Where(l => l.Incluir))
        {
            var e = global::BibliotecaDigital.App.Envios.Ler(S.Cfg, l.Id);
            if (e.Estado != EstadoDoEnvio.Pronto) { resultados.Add(new(e.NomeDoArquivo, null, e.Erro ?? "ainda não foi lido")); continue; }
            try
            {
                var colecao = S.Repositorio.Colecao(e.Colecao);
                var r = await S.Escrever(() =>
                {
                    var res = S.Importador.Importar(global::BibliotecaDigital.App.Envios.Pdf(S.Cfg, e.Id), colecao, e.Campos, por, "envio pela tela", nomeDoArquivo: e.NomeDoArquivo);
                    S.Indexador.IndexarDocumento(S.Cfg.Banco, res.Arquivos);
                    return res;
                });
                global::BibliotecaDigital.App.Envios.Descartar(S.Cfg, e.Id);
                resultados.Add(new(e.NomeDoArquivo, r.Arquivos.Id, null));
            }
            catch (InvalidOperationException ex) { resultados.Add(new(e.NomeDoArquivo, null, ex.Message)); }
        }
        Resultados = resultados;
        if (resultados.Count == 0) Erro = "Marque ao menos um arquivo para incluir.";
        try { CarregarLote(Lote); } catch (KeyNotFoundException) { Lote = null; }
        return Page();
    }

    public IActionResult OnPostDescartar()
    {
        if (Lote is not null) try { global::BibliotecaDigital.App.Envios.DescartarLote(S.Cfg, Lote); } catch (KeyNotFoundException) { }
        return Redirect("/curadoria/incluir");
    }

    /// <summary>Grava no envio o que a pessoa digitou na tabela, para não perder ao recarregar.</summary>
    private void Guardar()
    {
        foreach (var l in Linhas)
            try { global::BibliotecaDigital.App.Envios.AtualizarCampos(S.Cfg, l.Id, l.Campos.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim()), l.Nome); }
            catch (KeyNotFoundException) { }
    }

    private void CarregarLote(string id)
    {
        var lote = global::BibliotecaDigital.App.Envios.LerLote(S.Cfg, id);
        ColecaoDoLote = S.Repositorio.Colecao(lote.Colecao);
        Envios = lote.Envios.Select(e => { try { return global::BibliotecaDigital.App.Envios.Ler(S.Cfg, e); } catch (KeyNotFoundException) { return null; } })
            .Where(e => e is not null).Select(e => e!).ToList();
        if (Envios.Count == 0) { global::BibliotecaDigital.App.Envios.DescartarLote(S.Cfg, id); Lote = null; }
    }

    private void CarregarPrevia(string id)
    {
        Previa = global::BibliotecaDigital.App.Envios.Ler(S.Cfg, id);
        ColecaoDoLote = S.Repositorio.Colecao(Previa.Colecao);
        if (Previa.Estado != EstadoDoEnvio.Pronto) return;
        var texto = global::BibliotecaDigital.App.Envios.LerPrevia(S.Cfg, id);
        P = Math.Clamp(P, 1, Math.Max(1, texto.Paginas.Count));
        PaginaDaPrevia = texto.Paginas.FirstOrDefault(p => p.Numero == P);
    }

    /// <summary>Onde o arquivo vai ser gravado, ou o que impede.</summary>
    public (string Caminho, IReadOnlyList<string> Erros, bool Existe) Destino(Envio e)
    {
        var colecao = ColecaoDoLote!;
        var erros = colecao.ValidarCampos(e.Campos);
        var d = S.Repositorio.Destino(colecao, e.Campos, e.NomeDoArquivo);
        return ("acervo/" + Path.GetRelativePath(S.Repositorio.Raiz, d.Pdf).Replace('\\', '/'), erros, System.IO.File.Exists(d.Pdf));
    }
}
