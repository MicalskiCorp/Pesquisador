using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.App.Pages.Curadoria;

public sealed class FichaModel(Servicos servicos) : PaginaBase(servicos)
{
    public const int LinhasEmBranco = 5;

    [BindProperty(SupportsGet = true)] public string Id { get; set; } = "";
    [BindProperty] public string Titulo { get; set; } = "";
    [BindProperty] public Dictionary<string, string> Campos { get; set; } = new();
    /// <summary>Uma linha por ajuste: "página do PDF = impressa".</summary>
    [BindProperty] public string PaginasImpressas { get; set; } = "";
    [BindProperty] public List<Artigo> Artigos { get; set; } = [];
    [BindProperty] public string? Por { get; set; }

    public Ficha Ficha { get; private set; } = null!;
    public Colecao Colecao { get; private set; } = null!;
    public string Caminho { get; private set; } = "";
    public string? Erro { get; private set; }
    public string? Ok { get; private set; }

    public IActionResult OnGet(bool? salvo)
    {
        if (!Carregar()) return NotFound();
        Titulo = Ficha.Titulo;
        Campos = new Dictionary<string, string>(Ficha.Campos);
        PaginasImpressas = string.Join('\n', Ficha.PaginasImpressas.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} = {kv.Value}"));
        Artigos = Ficha.Artigos.ToList();
        if (salvo == true) Ok = "Ficha salva. O texto foi refeito e a edição reindexada.";
        return Page();
    }

    public async Task<IActionResult> OnPostSalvarAsync()
    {
        if (!Carregar()) return NotFound();
        var por = LembrarNome(Por);
        try
        {
            var ajustes = LerAjustes(PaginasImpressas);
            var artigos = Artigos.Where(a => !string.IsNullOrWhiteSpace(a.Titulo))
                .Select(a => a with { Titulo = a.Titulo.Trim(), Autor = Vazio(a.Autor), Secao = Vazio(a.Secao), Paginas = Vazio(a.Paginas) }).ToList();
            // Os campos que formam o caminho da pasta não mudam: mover arquivo por edição de
            // formulário é o tipo de efeito colateral que perde documento.
            foreach (var chave in Colecao.CamposDaPasta())
                if ((Campos.GetValueOrDefault(chave) ?? "") != (Ficha.Campos.GetValueOrDefault(chave) ?? ""))
                    throw new InvalidOperationException($"“{Colecao.Campos.FirstOrDefault(c => c.Chave == chave)?.Rotulo ?? chave}” define a pasta do documento e não se edita aqui. Para mudar, inclua o documento de novo.");
            Importador.Validar(Colecao, Campos);

            await S.Escrever(() =>
            {
                var d = S.Documento(Id);
                var atual = Ficha.Ler(d.Ficha);
                var mudou = new List<string>();
                if (Titulo.Trim() != atual.Titulo) mudou.Add("título");
                if (!Campos.OrderBy(k => k.Key).SequenceEqual(atual.Campos.OrderBy(k => k.Key))) mudou.Add("campos");
                if (!ajustes.OrderBy(k => k.Key).SequenceEqual(atual.PaginasImpressas.OrderBy(k => k.Key))) mudou.Add("páginas impressas");
                if (!artigos.SequenceEqual(atual.Artigos)) mudou.Add("artigos");
                if (mudou.Count == 0) return 0;
                var nova = atual with
                {
                    Titulo = Titulo.Trim(),
                    Campos = new Dictionary<string, string>(Campos),
                    PaginasImpressas = ajustes,
                    Artigos = artigos,
                    Historico = [.. atual.Historico, new EventoDaFicha(DateTimeOffset.Now, por, "ficha editada", string.Join(", ", mudou))],
                };
                nova.Gravar(d.Ficha);
                S.Importador.Reextrair(d);
                S.Indexador.IndexarDocumento(S.Cfg.Banco, d);
                return 1;
            });
            return Redirect($"/curadoria/ficha?id={Uri.EscapeDataString(Id)}&salvo=true");
        }
        catch (InvalidOperationException e) { Erro = e.Message; return Page(); }
    }

    public async Task<IActionResult> OnPostSubstituirAsync(IFormFile? arquivo)
    {
        if (!Carregar()) return NotFound();
        var por = LembrarNome(Por);
        if (arquivo is null || arquivo.Length == 0) { Erro = "Escolha o PDF novo."; return OnGet(null); }
        Directory.CreateDirectory(S.Cfg.Temporarios);
        var temp = Path.Combine(S.Cfg.Temporarios, Guid.NewGuid().ToString("N") + ".pdf");
        await using (var f = System.IO.File.Create(temp)) await arquivo.CopyToAsync(f);
        try
        {
            if (!Operacoes.EhPdf(temp)) throw new InvalidOperationException("O arquivo não é um PDF válido.");
            await S.Escrever(() =>
            {
                var d = S.Documento(Id);
                S.Importador.SubstituirPdf(d, temp, por);
                S.Indexador.IndexarDocumento(S.Cfg.Banco, d);
                return 0;
            });
            return Redirect($"/curadoria/ficha?id={Uri.EscapeDataString(Id)}&salvo=true");
        }
        catch (InvalidOperationException e) { Erro = e.Message; return OnGet(null); }
        finally { System.IO.File.Delete(temp); }
    }

    private bool Carregar()
    {
        var d = S.Repositorio.Localizar(Id);
        if (d is null) return false;
        Ficha = Ficha.Ler(d.Ficha);
        Colecao = S.Repositorio.Colecao(Ficha.Colecao);
        Caminho = "acervo/" + Path.GetRelativePath(S.Repositorio.Raiz, d.Pdf).Replace('\\', '/');
        return true;
    }

    private static Dictionary<int, string> LerAjustes(string texto)
    {
        var ajustes = new Dictionary<int, string>();
        foreach (var linha in texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var partes = linha.Split('=', 2, StringSplitOptions.TrimEntries);
            if (partes.Length != 2 || !int.TryParse(partes[0], out var pdf) || pdf < 1)
                throw new InvalidOperationException($"Ajuste de página inválido: “{linha}”. Use uma linha por ajuste, como “10 = 8”.");
            ajustes[pdf] = partes[1];
        }
        return ajustes;
    }

    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
