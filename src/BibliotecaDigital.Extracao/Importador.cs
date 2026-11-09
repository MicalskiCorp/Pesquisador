using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Extracao;

public sealed record ResultadoDaImportacao(ArquivosDoDocumento Arquivos, Ficha Ficha, ResultadoDaExtracao Extracao, bool Novo);

/// <summary>
/// Põe um PDF no acervo: copia o original para a pasta que a coleção manda, cria a ficha e
/// gera o texto. É o mesmo caminho para a migração em lote e para o cadastro pela tela —
/// por isso a inclusão manual e a automática produzem exatamente o mesmo resultado.
/// </summary>
public sealed class Importador(RepositorioDoAcervo repositorio, Extrator extrator)
{
    public RepositorioDoAcervo Repositorio => repositorio;

    /// <summary>
    /// Inclui ou atualiza. Se a ficha já existe, ela é preservada — é trabalho humano — e só o
    /// texto é refeito. Se o PDF de destino já existe e é outro arquivo, recusa: substituir
    /// original é uma ação explícita (<see cref="SubstituirPdf"/>), nunca efeito colateral.
    /// </summary>
    public ResultadoDaImportacao Importar(
        string pdfDeOrigem, Colecao colecao, IReadOnlyDictionary<string, string> campos,
        string por, string origem, string? urlPublica = null, string? nomeDoArquivo = null)
    {
        Validar(colecao, campos);
        var nome = nomeDoArquivo ?? Path.GetFileName(pdfDeOrigem);
        var destino = repositorio.Destino(colecao, campos, nome);
        var sha = ArquivoSeguro.Sha256(pdfDeOrigem);

        var copiado = false;
        if (File.Exists(destino.Pdf))
        {
            if (ArquivoSeguro.Sha256(destino.Pdf) != sha)
                throw new InvalidOperationException(
                    $"Já existe outro PDF em {Path.GetRelativePath(repositorio.Raiz, destino.Pdf)}. Use a substituição de PDF para trocar o original.");
        }
        else { ArquivoSeguro.Copiar(pdfDeOrigem, destino.Pdf); copiado = true; }

        var novo = !File.Exists(destino.Ficha);
        var ficha = novo
            ? NovaFicha(destino, colecao, campos, nome, sha, por, origem, urlPublica)
            : Ficha.Ler(destino.Ficha);

        ResultadoDaExtracao resultado;
        try { resultado = extrator.Extrair(destino.Pdf, colecao, ficha); }
        catch
        {
            // PDF que não abre não fica no acervo sem ficha (aconteceu com o de 1975, n.º 2, zerado).
            if (copiado) File.Delete(destino.Pdf);
            throw;
        }
        if (novo) ficha = ficha with { Pdf = ficha.Pdf with { Paginas = resultado.Paginas, Produtor = resultado.Documento.Valor("ocr") } };
        if (novo) ficha.Gravar(destino.Ficha);
        ArquivoSeguro.GravarTexto(destino.Texto, FormatoMarkdown.Gravar(resultado.Documento));
        return new ResultadoDaImportacao(destino, ficha, resultado, novo);
    }

    /// <summary>Refaz o <c>.md</c> a partir do PDF e da ficha. Nada humano se perde.</summary>
    public ResultadoDaExtracao Reextrair(ArquivosDoDocumento doc)
    {
        var ficha = Ficha.Ler(doc.Ficha);
        var colecao = repositorio.Colecao(ficha.Colecao);
        var r = extrator.Extrair(doc.Pdf, colecao, ficha);
        ArquivoSeguro.GravarTexto(doc.Texto, FormatoMarkdown.Gravar(r.Documento));
        return r;
    }

    /// <summary>Troca o PDF original, arquivando o anterior ao lado com a data, e refaz o texto.</summary>
    public ResultadoDaExtracao SubstituirPdf(ArquivosDoDocumento doc, string novoPdf, string por)
    {
        var ficha = Ficha.Ler(doc.Ficha);
        var arquivado = Path.Combine(doc.Pasta, ".substituidos", $"{doc.NomeBase}.{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(arquivado)!);
        File.Copy(doc.Pdf, arquivado);
        ArquivoSeguro.Copiar(novoPdf, doc.Pdf);

        var sha = ArquivoSeguro.Sha256(doc.Pdf);
        ficha = ficha with
        {
            Pdf = ficha.Pdf with { Sha256 = sha, Bytes = new FileInfo(doc.Pdf).Length },
            Historico = [.. ficha.Historico, new EventoDaFicha(DateTimeOffset.Now, por, "pdf substituído", $"anterior em .substituidos/{Path.GetFileName(arquivado)}")],
        };
        ficha.Gravar(doc.Ficha);
        return Reextrair(doc);
    }

    public static void Validar(Colecao colecao, IReadOnlyDictionary<string, string> campos)
    {
        var erros = colecao.ValidarCampos(campos);
        if (erros.Count > 0) throw new InvalidOperationException(string.Join(" ", erros));
    }

    private static Ficha NovaFicha(ArquivosDoDocumento destino, Colecao colecao, IReadOnlyDictionary<string, string> campos,
        string nome, string sha, string por, string origem, string? urlPublica) => new()
    {
        Id = destino.Id,
        Colecao = colecao.Id,
        Titulo = colecao.TituloDoDocumento(campos, nome),
        Campos = new Dictionary<string, string>(campos),
        Pdf = new DadosDoPdf
        {
            Arquivo = Path.GetFileName(destino.Pdf),
            Bytes = new FileInfo(destino.Pdf).Length,
            Sha256 = sha,
            Paginas = 0,
            Origem = origem,
            UrlPublica = urlPublica,
        },
        Historico = [new EventoDaFicha(DateTimeOffset.Now, por, "incluído", origem)],
    };
}
