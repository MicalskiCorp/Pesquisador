using System.Globalization;
using System.Text;
using System.Text.Json;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.App;

/// <summary>Uma edição que deveria existir e não está no acervo.</summary>
public sealed record Desfalque(string Grupo, string? Ano, string Numero, string Situacao, string? Observacao);

/// <summary>
/// Que edições faltam numa coleção de periódico, pela numeração dela (<see cref="NumeracaoDaColecao"/>):
/// em cada grupo (o tomo da revista), os números cobertos pelas edições presentes ("11-12" cobre 11
/// e 12) contra a sequência de 1 a N do grupo. Também o ano sem edição nenhuma entre o primeiro e o
/// último, e os anos depois do último grupo até o ano corrente. Coleção sem numeração não tem
/// desfalques — livro e documento avulso não têm "próximo número".
///
/// A lista só diminui quando o documento entra no acervo. Observações sobre cada desfalque ficam
/// em <c>acervo/desfalques.json</c> (por coleção), editável.
/// </summary>
public static class Desfalques
{
    public const string ArquivoDeObservacoes = "desfalques.json";

    public static IReadOnlyList<Desfalque> Calcular(RepositorioDoAcervo repo, Colecao colecao, int anoCorrente)
    {
        var fichas = repo.Documentos().Select(d => Ficha.Ler(d.Ficha)).Where(f => f.Colecao == colecao.Id).ToList();
        return Calcular(colecao, fichas.Select(f => (IReadOnlyDictionary<string, string>)f.Campos).ToList(), anoCorrente, Observacoes(repo, colecao.Id));
    }

    public static IReadOnlyList<Desfalque> Calcular(Colecao colecao, IReadOnlyList<IReadOnlyDictionary<string, string>> fichas,
        int anoCorrente, IReadOnlyDictionary<string, string>? observacoes = null)
    {
        if (colecao.Numeracao is not { } num) return [];
        var obs = observacoes ?? new Dictionary<string, string>();
        var porGrupo = fichas
            .Select(c => (Grupo: Numeros.Valor(c.GetValueOrDefault(num.Grupo)), Texto: c.GetValueOrDefault(num.Grupo), Campos: c))
            .Where(x => x.Grupo is not null)
            .GroupBy(x => x.Grupo!.Value)
            .OrderBy(g => g.Key)
            .ToList();

        var lista = new List<Desfalque>();
        string? exemplo = null;
        foreach (var g in porGrupo)
        {
            var rotulo = g.First().Texto!.Trim();
            exemplo = rotulo;
            var cobertos = g.SelectMany(x => Numeros.Cobertos(x.Campos.GetValueOrDefault(num.Numero))).ToHashSet();
            var anos = g.Select(x => colecao.Ano(x.Campos)).Where(a => a is not null).Select(a => a!.Value).Distinct().Order().ToList();
            var edicoes = num.EdicoesPorGrupo(anos.Count > 0 ? anos[0] : null);
            // Zeros à esquerda até a largura do maior número (02 numa revista, 002 num diário), para a
            // lista sair em ordem numérica.
            var formato = new string('0', Math.Max(2, edicoes.ToString(CultureInfo.InvariantCulture).Length));
            foreach (var n in Enumerable.Range(1, edicoes).Where(n => !cobertos.Contains(n)))
            {
                var numero = n.ToString(formato, CultureInfo.InvariantCulture);
                lista.Add(new Desfalque(rotulo, string.Join("/", anos), numero, "número sem edição no acervo", obs.GetValueOrDefault($"{rotulo}/{numero}")));
            }
        }

        var anosPresentes = fichas.Select(colecao.Ano).Where(a => a is not null).Select(a => a!.Value).ToHashSet();
        if (anosPresentes.Count > 0)
        {
            var ultimoGrupo = porGrupo.Count > 0 ? porGrupo[^1].Key : 0;
            for (var a = anosPresentes.Min(); a <= anoCorrente; a++)
            {
                if (anosPresentes.Contains(a)) continue;
                var depois = a > anosPresentes.Max();
                var grupo = depois && ultimoGrupo > 0 ? Numeros.ComoExemplo(ultimoGrupo + (a - anosPresentes.Max()), exemplo) : "—";
                var chave = a.ToString(CultureInfo.InvariantCulture);
                lista.Add(new Desfalque(grupo, chave, "todas",
                    depois ? "ano posterior à última edição do acervo" : "ano sem nenhuma edição no acervo", obs.GetValueOrDefault(chave)));
            }
        }
        return lista.OrderBy(d => d.Ano, StringComparer.Ordinal).ThenBy(d => d.Numero, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Observações da coleção. O arquivo guarda um objeto por coleção; o formato antigo (as
    /// observações direto na raiz) vale para a primeira coleção do acervo.
    /// </summary>
    public static Dictionary<string, string> Observacoes(RepositorioDoAcervo repo, string colecao)
    {
        var arquivo = Path.Combine(repo.Raiz, ArquivoDeObservacoes);
        if (!File.Exists(arquivo)) return [];
        using var doc = JsonDocument.Parse(File.ReadAllText(arquivo));
        var raiz = doc.RootElement;
        if (raiz.TryGetProperty(colecao, out var daColecao) && daColecao.ValueKind == JsonValueKind.Object)
            return daColecao.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        return [];
    }

    /// <summary>A relação em Markdown, para entregar à instituição: todas as coleções com numeração.</summary>
    public static string Relatorio(IReadOnlyList<(Colecao Colecao, int Documentos, IReadOnlyList<Desfalque> Lista)> colecoes, DateTime quando)
    {
        var sb = new StringBuilder();
        sb.Append("# Desfalques do acervo\n\n");
        sb.Append($"Gerado pela Biblioteca Digital em {quando:dd/MM/yyyy}. As edições abaixo deveriam existir pela numeração ");
        sb.Append("de cada periódico e não estão no acervo. A lista só diminui quando o documento entra no acervo.\n\n");
        sb.Append("Para incluir: digitalizar com OCR e enviar pela tela de Curadoria da Biblioteca Digital, ou entregar o PDF para inclusão.\n");
        foreach (var (colecao, documentos, lista) in colecoes)
        {
            sb.Append($"\n## {colecao.Nome}\n\n{documentos} documentos no acervo. ");
            if (lista.Count == 0) { sb.Append("Nenhum desfalque: a numeração está completa.\n"); continue; }
            sb.Append($"{lista.Count} desfalques.\n\n");
            sb.Append($"| {Rotulo(colecao)} | Ano | Número | Situação | Observação |\n|---|---|---|---|---|\n");
            foreach (var d in lista)
                sb.Append($"| {d.Grupo} | {d.Ano} | {d.Numero} | {d.Situacao} | {d.Observacao ?? ""} |\n");
            var num = colecao.Numeracao!;
            sb.Append($"\nCritério: em cada {Rotulo(colecao).ToLowerInvariant()}, os números cobertos pelas edições presentes contra a sequência de ");
            sb.Append(string.Join("; ", num.Faixas.Select(f => f.AteAno is { } ate ? $"1 a {f.Edicoes} até {ate}" : $"1 a {f.Edicoes}" + (num.Faixas.Count > 1 ? " depois" : ""))));
            sb.Append(". Edição conjunta (\"11-12\") cobre os dois números.\n");
        }
        return sb.ToString();
    }

    public static string Rotulo(Colecao c) =>
        c.Campos.FirstOrDefault(x => x.Chave == c.Numeracao?.Grupo)?.Rotulo ?? "Grupo";
}
