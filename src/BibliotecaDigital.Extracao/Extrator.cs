using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Extracao;

/// <summary>Uma página divergente e o que não bateu, para a auditoria.</summary>
public sealed record Divergencia(int Pagina, IReadOnlyDictionary<string, int> Faltando, IReadOnlyDictionary<string, int> Sobrando);

public sealed record ResultadoDaExtracao(DocumentoTexto Documento, int PaginasConferidas, int PaginasSemTexto, IReadOnlyList<Divergencia> Divergencias)
{
    public int Paginas => Documento.Paginas.Count;
    public bool TemTexto => Documento.Paginas.Any(p => p.Paragrafos.Count > 0 || p.Margens.Count > 0);
}

/// <summary>
/// PDF + ficha → <c>.md</c>. Determinístico: o mesmo PDF, a mesma ficha e a mesma versão do
/// extrator geram sempre o mesmo arquivo, byte por byte (não há data no texto gerado).
/// </summary>
public sealed class Extrator(LeitorPoppler poppler, Lexico? lexico = null)
{
    /// <summary>Muda quando uma regra de montagem muda. Fica gravada em todo <c>.md</c>.</summary>
    public const string Versao = "1.0.1";

    public LeitorPoppler Poppler => poppler;

    /// <summary>Só a geometria — usada para montar o léxico, sem conferência.</summary>
    public void ColetarLexico(string pdf, ColetorDeLexico coletor) => coletor.Contar(poppler.Ler(pdf));

    public ResultadoDaExtracao Extrair(string pdf, Colecao colecao, Ficha ficha)
    {
        var camada = LeitorDaCamadaDeTexto.Ler(pdf);
        var layouts = poppler.Ler(pdf);
        var montador = new MontadorDePagina(colecao.Extracao, lexico);

        var montadas = new List<PaginaMontada>();
        var conferencias = new List<ResultadoDaConferencia>();
        for (var i = 0; i < camada.Paginas; i++)
        {
            var layout = i < layouts.Count ? layouts[i] : new PaginaLayout(i + 1, 0, 0, []);
            var m = montador.Montar(layout);
            montadas.Add(m);
            conferencias.Add(ConferenciaDeFidelidade.Conferir(layout, camada.PosicoesPorPagina[i], m, colecao.Extracao.DesfazerMeiaLargura));
        }

        var impressas = PaginacaoImpressa.Calcular(montadas.Select(m => m.Margens).ToList(), ficha.PaginasImpressas);

        var paginas = new List<PaginaTexto>();
        var divergencias = new List<Divergencia>();
        for (var i = 0; i < montadas.Count; i++)
        {
            var m = montadas[i];
            var c = conferencias[i];
            if (c.Fidelidade == Fidelidade.Divergente)
                divergencias.Add(new Divergencia(i + 1, c.Faltando, c.Sobrando));
            paginas.Add(new PaginaTexto
            {
                Numero = i + 1,
                Impressa = impressas[i].Impressa,
                OrigemImpressa = impressas[i].Impressa is null ? null : impressas[i].Origem,
                Fidelidade = c.Fidelidade,
                Colunas = m.Colunas,
                HifensRemovidos = m.HifensRemovidos.Count,
                MeiaLargura = m.MeiaLargura,
                NaoLidos = c.NaoLidos,
                Margens = m.Margens,
                Paragrafos = m.Paragrafos,
            });
        }

        var conferidas = paginas.Count(p => p.Fidelidade == Fidelidade.Conferida);
        var semTexto = paginas.Count(p => p.Fidelidade == Fidelidade.SemTexto);

        var cabecalho = new List<KeyValuePair<string, string>>
        {
            new("formato", FormatoMarkdown.Versao),
            new("id", ficha.Id),
            new("colecao", ficha.Colecao),
            new("titulo", ficha.Titulo),
        };
        foreach (var campo in colecao.Campos)
            if (ficha.Campos.TryGetValue(campo.Chave, out var v) && v.Length > 0)
                cabecalho.Add(new(campo.Chave, v));
        cabecalho.Add(new("pdf", ficha.Pdf.Arquivo));
        cabecalho.Add(new("pdf_sha256", ficha.Pdf.Sha256));
        cabecalho.Add(new("pdf_paginas", camada.Paginas.ToString()));
        if (camada.Produtor is { } prod) cabecalho.Add(new("ocr", prod));
        cabecalho.Add(new("extrator", Versao));
        cabecalho.Add(new("fidelidade", $"{conferidas} conferidas, {divergencias.Count} divergentes, {semTexto} sem texto"));

        var doc = new DocumentoTexto { Cabecalho = cabecalho, Titulo = ficha.Titulo, Paginas = paginas };
        return new ResultadoDaExtracao(doc, conferidas, semTexto, divergencias);
    }
}
