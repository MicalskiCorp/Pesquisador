using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Extracao;

public enum Gravidade { Nota, Atencao, Problema }

public sealed record Achado(Gravidade Gravidade, string Documento, string Mensagem);

public sealed record RelatorioDeAuditoria(int Documentos, int Paginas, int Conferidas, int Divergentes, int SemTexto, IReadOnlyList<Achado> Achados)
{
    public bool SemProblemas => Achados.All(a => a.Gravidade == Gravidade.Nota);
}

/// <summary>
/// Confere o acervo inteiro contra ele mesmo: todo PDF tem ficha e texto, o PDF é o mesmo
/// que a ficha registrou (SHA-256), o texto foi gerado deste PDF e pela versão atual do
/// extrator, e quantas páginas passaram na conferência de fidelidade.
/// </summary>
public static class Auditoria
{
    /// <summary>A partir de quantos glifos não lidos uma página vale um olhar humano.</summary>
    public const int LimiteDeNaoLidos = 30;

    public static RelatorioDeAuditoria Auditar(RepositorioDoAcervo repo, bool conferirHash = true)
    {
        var achados = new List<Achado>();
        int docs = 0, paginas = 0, conferidas = 0, divergentes = 0, semTexto = 0;
        var ids = new HashSet<string>();
        var colecoes = repo.Colecoes().Select(c => c.Id).ToHashSet();

        foreach (var d in repo.Documentos())
        {
            docs++;
            if (!ids.Add(d.Id)) achados.Add(new(Gravidade.Problema, d.Id, "identificador repetido"));
            Ficha ficha;
            try { ficha = Ficha.Ler(d.Ficha); }
            catch (Exception e) { achados.Add(new(Gravidade.Problema, d.Id, $"ficha ilegível: {e.Message}")); continue; }

            if (!colecoes.Contains(ficha.Colecao)) achados.Add(new(Gravidade.Problema, d.Id, $"coleção '{ficha.Colecao}' não está em colecoes.json"));
            if (!File.Exists(d.Pdf)) { achados.Add(new(Gravidade.Problema, d.Id, "PDF original ausente")); continue; }
            if (conferirHash && ArquivoSeguro.Sha256(d.Pdf) != ficha.Pdf.Sha256)
                achados.Add(new(Gravidade.Problema, d.Id, "o PDF no disco não é o que a ficha registrou (SHA-256 diferente)"));
            if (!File.Exists(d.Texto)) { achados.Add(new(Gravidade.Problema, d.Id, "texto (.md) ausente — rode reextrair")); continue; }

            var texto = FormatoMarkdown.Ler(File.ReadAllText(d.Texto));
            if (texto.Valor("pdf_sha256") != ficha.Pdf.Sha256)
                achados.Add(new(Gravidade.Problema, d.Id, "o texto foi gerado de outro PDF — rode reextrair"));
            if (texto.Valor("extrator") != Extrator.Versao)
                achados.Add(new(Gravidade.Atencao, d.Id, $"texto da versão {texto.Valor("extrator")} do extrator (atual: {Extrator.Versao}) — rode reextrair"));
            if (texto.Paginas.Count != ficha.Pdf.Paginas)
                achados.Add(new(Gravidade.Atencao, d.Id, $"a ficha diz {ficha.Pdf.Paginas} páginas e o texto tem {texto.Paginas.Count}"));

            paginas += texto.Paginas.Count;
            conferidas += texto.Paginas.Count(p => p.Fidelidade == Fidelidade.Conferida);
            semTexto += texto.Paginas.Count(p => p.Fidelidade == Fidelidade.SemTexto);
            var div = texto.Paginas.Where(p => p.Fidelidade == Fidelidade.Divergente).Select(p => p.Numero).ToList();
            divergentes += div.Count;
            if (div.Count > 0)
                achados.Add(new(Gravidade.Atencao, d.Id, $"fidelidade divergente nas páginas {string.Join(", ", div)}"));
            var revisar = texto.Paginas.Where(p => p.NaoLidos >= LimiteDeNaoLidos).Select(p => $"{p.Numero} ({p.NaoLidos})").ToList();
            if (revisar.Count > 0)
                achados.Add(new(Gravidade.Nota, d.Id, $"glifos do PDF não lidos — conferir na imagem: p. {string.Join(", ", revisar)}"));
            if (texto.Paginas.Count > 0 && texto.Paginas.All(p => p.Fidelidade == Fidelidade.SemTexto))
                achados.Add(new(Gravidade.Nota, d.Id, "PDF sem camada de texto: aparece no acervo, mas não na busca"));
        }

        // PDF sem ficha: entrou no disco por fora do sistema.
        if (Directory.Exists(repo.Raiz))
            foreach (var pdf in Directory.EnumerateFiles(repo.Raiz, "*.pdf", SearchOption.AllDirectories))
            {
                if (pdf.Contains($"{Path.DirectorySeparatorChar}.substituidos{Path.DirectorySeparatorChar}")) continue;
                if (!File.Exists(Path.ChangeExtension(pdf, null) + ".ficha.json"))
                    achados.Add(new(Gravidade.Problema, Path.GetRelativePath(repo.Raiz, pdf), "PDF sem ficha — incluir pelo sistema"));
            }

        return new RelatorioDeAuditoria(docs, paginas, conferidas, divergentes, semTexto, achados);
    }
}
