using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.App.Pages.Curadoria;

public sealed class ErrataModel(Servicos servicos) : PaginaBase(servicos)
{
    [BindProperty(SupportsGet = true)] public string Id { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Literal { get; set; }
    [BindProperty] public string? Leitura { get; set; }
    [BindProperty] public string? Por { get; set; }

    public Ficha Ficha { get; private set; } = null!;
    public string? Erro { get; private set; }
    public string? Ok { get; private set; }

    public IActionResult OnGet(bool? gravada)
    {
        var d = S.Repositorio.Localizar(Id);
        if (d is null) return NotFound();
        Ficha = Ficha.Ler(d.Ficha);
        if (gravada == true) Ok = "Errata registrada. A busca já encontra a leitura correta.";
        return Page();
    }

    public async Task<IActionResult> OnPostRegistrarAsync()
    {
        var por = LembrarNome(Por);
        try
        {
            await S.Escrever(() =>
            {
                var d = S.Documento(Id);
                var texto = FormatoMarkdown.Ler(System.IO.File.ReadAllText(d.Texto));
                var pagina = texto.Paginas.FirstOrDefault(p => p.Numero == Pagina)
                    ?? throw new InvalidOperationException($"A página {Pagina} não existe nesta edição.");
                var literal = (Literal ?? "").Trim();
                // A errata se ancora no texto literal: corrigir o que não está na página não é corrigir.
                if (literal.Length == 0 || !(pagina.Corpo + "\n" + string.Join('\n', pagina.Margens)).Contains(literal, StringComparison.Ordinal))
                    throw new InvalidOperationException($"O trecho “{literal}” não está na página {Pagina}. Copie-o exatamente como aparece no texto.");
                if (string.IsNullOrWhiteSpace(Leitura)) throw new InvalidOperationException("Informe a leitura correta, como está na imagem do PDF.");
                var ficha = Ficha.Ler(d.Ficha);
                var nova = new Errata { Id = Guid.NewGuid().ToString("N")[..12], Pagina = Pagina, Literal = literal, Leitura = Leitura.Trim(), Por = por, Em = DateTimeOffset.Now };
                ficha = ficha with
                {
                    Errata = [.. ficha.Errata, nova],
                    Historico = [.. ficha.Historico, new EventoDaFicha(nova.Em, por, "errata", $"p. {nova.Pagina}: “{nova.Literal}” → “{nova.Leitura}”")],
                };
                ficha.Gravar(d.Ficha);
                S.Indexador.IndexarDocumento(S.Cfg.Banco, d);
                return 0;
            });
            return Redirect($"/curadoria/errata?id={Uri.EscapeDataString(Id)}&pagina={Pagina}&gravada=true");
        }
        catch (InvalidOperationException e) { Erro = e.Message; return OnGet(null); }
    }

    public async Task<IActionResult> OnPostRemoverAsync(string errata)
    {
        var por = LembrarNome(Por ?? Nome);
        await S.Escrever(() =>
        {
            var d = S.Documento(Id);
            var ficha = Ficha.Ler(d.Ficha);
            var alvo = ficha.Errata.FirstOrDefault(x => x.Id == errata);
            if (alvo is null) return 0;
            ficha = ficha with
            {
                Errata = ficha.Errata.Where(x => x.Id != errata).ToList(),
                Historico = [.. ficha.Historico, new EventoDaFicha(DateTimeOffset.Now, por, "errata removida", $"p. {alvo.Pagina}: “{alvo.Literal}”")],
            };
            ficha.Gravar(d.Ficha);
            S.Indexador.IndexarDocumento(S.Cfg.Banco, d);
            return 0;
        });
        return Redirect($"/curadoria/errata?id={Uri.EscapeDataString(Id)}&pagina={Pagina}");
    }
}
