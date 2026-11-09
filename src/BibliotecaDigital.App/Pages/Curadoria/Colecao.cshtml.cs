using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.App.Pages.Curadoria;

/// <summary>Uma linha da tabela de campos do formulário de coleção.</summary>
public sealed class CampoEditavel
{
    public string? Rotulo { get; set; }
    public string? Chave { get; set; }
    public string Tipo { get; set; } = TiposDeCampo.Texto;
    public bool Obrigatorio { get; set; }
    public string? Opcoes { get; set; }
    public string? Ajuda { get; set; }
}

/// <summary>Cadastro e edição de uma coleção (um tipo de acervo). Grava em <c>acervo/colecoes.json</c>.</summary>
public sealed class ColecaoModel(Servicos servicos) : PaginaBase(servicos)
{
    public const int LinhasEmBranco = 4;

    /// <summary>Coleção em edição; vazio é coleção nova.</summary>
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Modelo { get; set; }

    [BindProperty] public string NovoId { get; set; } = "";
    [BindProperty] public string NomeDaColecao { get; set; } = "";
    [BindProperty] public string? Descricao { get; set; }
    [BindProperty] public string Pastas { get; set; } = "";
    [BindProperty] public string Titulo { get; set; } = "{arquivo}";
    [BindProperty] public List<CampoEditavel> Campos { get; set; } = [];
    [BindProperty] public string? NomesDeArquivo { get; set; }
    [BindProperty] public string? Margens { get; set; }
    [BindProperty] public bool DesfazerMeiaLargura { get; set; } = true;
    [BindProperty] public bool TemNumeracao { get; set; }
    [BindProperty] public string? Grupo { get; set; }
    [BindProperty] public string? Numero { get; set; }
    [BindProperty] public string? Faixas { get; set; }
    [BindProperty] public string? Por { get; set; }

    public bool Nova => string.IsNullOrEmpty(Id);
    public int Documentos { get; private set; }
    public IReadOnlyList<string> Erros { get; private set; } = [];

    public static readonly (string Valor, string Nome)[] OpcoesDePasta =
    [
        ("", "Tudo na pasta da coleção"),
        ("{ano}", "Uma pasta por ano"),
        ("{decada}/{ano}", "Por década e ano"),
        ("{ano}/{mes}", "Por ano e mês (precisa de campo de data)"),
    ];

    public IActionResult OnGet()
    {
        Colecao? origem;
        if (!Nova)
        {
            origem = S.Repositorio.Colecoes().FirstOrDefault(c => c.Id == Id);
            if (origem is null) return NotFound();
        }
        else origem = (ModelosDeColecao.Todos.FirstOrDefault(m => m.Id == Modelo) ?? ModelosDeColecao.Todos[^1]).Criar();
        Preencher(origem);
        Documentos = Nova ? 0 : S.Repositorio.Documentos().Count(d => d.Colecao == Id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var por = LembrarNome(Por);
        var existentes = S.Repositorio.Colecoes();
        Documentos = Nova ? 0 : S.Repositorio.Documentos().Count(d => d.Colecao == Id);
        var colecao = Montar();
        var outras = existentes.Where(c => c.Id != (Nova ? null : Id)).ToList();
        var erros = Colecoes.Validar(colecao, outras).ToList();
        // O identificador entra no caminho de cada arquivo: com documentos, não muda.
        if (!Nova && Documentos > 0 && colecao.Id != Id) erros.Add("Esta coleção já tem documentos: o identificador não pode mudar.");
        if (erros.Count > 0)
        {
            Erros = erros;
            Campos = Campos.Where(c => !string.IsNullOrWhiteSpace(c.Rotulo) || !string.IsNullOrWhiteSpace(c.Chave)).ToList();
            return Page();
        }

        await S.Escrever(() =>
        {
            var lista = existentes.ToList();
            var i = lista.FindIndex(c => c.Id == Id);
            if (i >= 0) lista[i] = colecao; else lista.Add(colecao);
            Colecoes.Gravar(Path.Combine(S.Repositorio.Raiz, RepositorioDoAcervo.ArquivoDeColecoes), lista);
            return 0;
        });
        HttpContext.RequestServices.GetRequiredService<ILogger<ColecaoModel>>()
            .LogInformation("Coleção {Id} {Acao} por {Por}", colecao.Id, Nova ? "criada" : "editada", por);
        return Redirect("/curadoria/colecoes?msg=" + Uri.EscapeDataString($"Coleção “{colecao.Nome}” {(Nova ? "criada" : "salva")}."));
    }

    private void Preencher(Colecao c)
    {
        NovoId = c.Id;
        NomeDaColecao = c.Nome;
        Descricao = c.Descricao;
        Pastas = c.Pastas;
        Titulo = c.Titulo;
        Campos = c.Campos.Select(x => new CampoEditavel
        {
            Rotulo = x.Rotulo, Chave = x.Chave, Tipo = x.Tipo, Obrigatorio = x.Obrigatorio,
            Opcoes = x.Opcoes is null ? null : string.Join(", ", x.Opcoes), Ajuda = x.Ajuda,
        }).ToList();
        NomesDeArquivo = string.Join('\n', c.NomesDeArquivo);
        Margens = string.Join('\n', c.Extracao.Margens);
        DesfazerMeiaLargura = c.Extracao.DesfazerMeiaLargura;
        TemNumeracao = c.Numeracao is not null;
        Grupo = c.Numeracao?.Grupo;
        Numero = c.Numeracao?.Numero;
        Faixas = c.Numeracao is { } n ? string.Join('\n', n.Faixas.Select(f => f.AteAno is { } a ? $"{f.Edicoes} até {a}" : $"{f.Edicoes}")) : "12";
    }

    /// <summary>Do formulário para a coleção. Chave vazia sai do rótulo; identificador vazio, do nome.</summary>
    private Colecao Montar()
    {
        var id = string.IsNullOrWhiteSpace(NovoId) ? Texto.Slug(NomeDaColecao) : NovoId.Trim();
        if (!Nova && Documentos > 0) id = Id!;
        var campos = Campos.Where(c => !string.IsNullOrWhiteSpace(c.Rotulo) || !string.IsNullOrWhiteSpace(c.Chave))
            .Select(c => new CampoDaColecao(
                string.IsNullOrWhiteSpace(c.Chave) ? Texto.Slug(c.Rotulo ?? "").Replace('-', '_') : c.Chave.Trim(),
                (c.Rotulo ?? c.Chave ?? "").Trim(), c.Obrigatorio, string.IsNullOrWhiteSpace(c.Ajuda) ? null : c.Ajuda.Trim())
            {
                Tipo = c.Tipo,
                Opcoes = c.Tipo == TiposDeCampo.Opcoes
                    ? (c.Opcoes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : null,
            }).ToList();
        return new Colecao
        {
            Id = id,
            Nome = NomeDaColecao.Trim(),
            Descricao = string.IsNullOrWhiteSpace(Descricao) ? null : Descricao.Trim(),
            Pastas = (Pastas ?? "").Trim(),
            Titulo = string.IsNullOrWhiteSpace(Titulo) ? "{arquivo}" : Titulo.Trim(),
            Campos = campos,
            NomesDeArquivo = Linhas(NomesDeArquivo),
            Extracao = new OpcoesDeExtracao
            {
                DesfazerMeiaLargura = DesfazerMeiaLargura,
                // Texto simples quer dizer "linha que começa com isto"; com ^ é expressão regular.
                Margens = Linhas(Margens).Select(m => m.StartsWith('^') ? m : "^" + Regex.Escape(m).Replace("\\ ", " ")).ToList(),
            },
            // Marcada sem os campos, a numeração vai assim mesmo: a validação diz o que falta.
            Numeracao = TemNumeracao
                ? new NumeracaoDaColecao { Grupo = (Grupo ?? "").Trim(), Numero = (Numero ?? "").Trim(), Faixas = LerFaixas(Faixas) }
                : null,
        };
    }

    private static List<string> Linhas(string? texto) =>
        (texto ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>"12 até 2007" e "6": doze edições por grupo até 2007, seis depois.</summary>
    private static List<FaixaDeNumeracao> LerFaixas(string? texto) =>
        Linhas(texto).Select(l => Regex.Match(l, @"^(\d+)(?:\s*(?:até|ate)\s*(\d{4}))?$"))
            .Where(m => m.Success)
            .Select(m => new FaixaDeNumeracao(int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : null))
            .OrderBy(f => f.AteAno ?? int.MaxValue)
            .ToList();
}
