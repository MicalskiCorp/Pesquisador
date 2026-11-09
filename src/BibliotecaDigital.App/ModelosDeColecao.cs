using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.App;

/// <summary>
/// Pontos de partida para cadastrar um tipo de acervo. São só sugestões de campos, pastas e título —
/// tudo se ajusta no formulário antes de gravar.
/// </summary>
public static class ModelosDeColecao
{
    public sealed record Modelo(string Id, string Nome, string Descricao, Func<Colecao> Criar);

    public static readonly IReadOnlyList<Modelo> Todos =
    [
        new("periodico", "Periódico", "Revista, jornal, boletim: sai em edições numeradas e datadas. Guarda por ano; calcula desfalques pela numeração.", () => new Colecao
        {
            Id = "", Nome = "",
            Pastas = "{ano}",
            Titulo = "{colecao} — n.º {numero} ({data})",
            Campos =
            [
                new CampoDaColecao("data", "Data", true, "Data da edição: dd/mm/aaaa, mm/aaaa ou só o ano.") { Tipo = TiposDeCampo.Data },
                new CampoDaColecao("volume", "Volume / ano da publicação", false, "Como impresso: romano ou arábico."),
                new CampoDaColecao("numero", "Número", true, "Como impresso: 12, 11-12."),
                new CampoDaColecao("caderno", "Caderno / seção"),
            ],
            Numeracao = new NumeracaoDaColecao { Grupo = "volume", Numero = "numero", Faixas = [new FaixaDeNumeracao(12)] },
        }),
        new("livro", "Livro", "Obra única, com título e autor. Sem subpastas.", () => new Colecao
        {
            Id = "", Nome = "",
            Pastas = "",
            Titulo = "{titulo}",
            Campos =
            [
                new CampoDaColecao("titulo", "Título", true),
                new CampoDaColecao("autor", "Autor"),
                new CampoDaColecao("ano", "Ano", false) { Tipo = TiposDeCampo.Ano },
                new CampoDaColecao("editora", "Editora"),
            ],
        }),
        new("avulso", "Documento avulso", "Ofício, ata, carta, relatório: documento único com data e procedência. Sem subpastas.", () => new Colecao
        {
            Id = "", Nome = "",
            Pastas = "",
            Titulo = "{titulo}",
            Campos =
            [
                new CampoDaColecao("titulo", "Título / assunto", true),
                new CampoDaColecao("data", "Data") { Tipo = TiposDeCampo.Data },
                new CampoDaColecao("procedencia", "Procedência", false, "De onde veio: fundo, doador, instituição."),
            ],
        }),
        new("vazio", "Em branco", "Comece do zero.", () => new Colecao { Id = "", Nome = "", Pastas = "", Titulo = "{arquivo}" }),
    ];
}
