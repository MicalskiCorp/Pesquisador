using System.Text.Json;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Busca;

namespace BibliotecaDigital.App;

/// <summary>
/// A navegação pelo acervo, lida do índice (rápido) — o acervo em disco é a fonte dele. Genérica:
/// coleção → década → ano para o que é datado; coleção → lista por título para o que não é.
/// </summary>
public static class Navegacao
{
    public sealed record Item(string Id, string Titulo, int? Ano, Dictionary<string, string> Campos, int Paginas);
    public sealed record Ano(int? Numero, IReadOnlyList<Item> Itens);
    public sealed record Decada(string Nome, int Total, IReadOnlyList<Ano> Anos);
    public sealed record ResumoDaColecao(Colecao Colecao, int Documentos, int Paginas, int? Primeiro, int? Ultimo);

    /// <summary>Cada coleção configurada, com o que o índice tem dela (zero se ainda não tem nada).</summary>
    public static IReadOnlyList<ResumoDaColecao> Resumo(Servicos s)
    {
        var colecoes = s.Repositorio.Colecoes();
        var dados = new Dictionary<string, (int Docs, int Pags, int? Min, int? Max)>();
        if (File.Exists(s.Cfg.Banco))
        {
            using var c = BancoDeBusca.Abrir(s.Cfg.Banco, somenteLeitura: true);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT colecao, count(*), sum(paginas), min(ano), max(ano) FROM documento GROUP BY colecao";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                dados[r.GetString(0)] = (r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4));
        }
        return colecoes.Select(col => dados.TryGetValue(col.Id, out var d)
                ? new ResumoDaColecao(col, d.Docs, d.Pags, d.Min, d.Max)
                : new ResumoDaColecao(col, 0, 0, null, null))
            .ToList();
    }

    public static IReadOnlyList<Decada> Arvore(Servicos s, string colecao)
    {
        if (!File.Exists(s.Cfg.Banco)) return [];
        using var c = BancoDeBusca.Abrir(s.Cfg.Banco, somenteLeitura: true);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT chave, titulo, ano, campos, paginas FROM documento WHERE colecao = $c ORDER BY ano, nome_base";
        cmd.Parameters.AddWithValue("$c", colecao);
        var itens = new List<Item>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                itens.Add(new Item(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2),
                    JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(3)) ?? [], r.GetInt32(4)));

        // Sem data nenhuma na coleção: uma lista só, por título.
        if (itens.All(i => i.Ano is null))
            return itens.Count == 0 ? [] : [new Decada("Todos", itens.Count, [new Ano(null, itens.OrderBy(i => i.Titulo, StringComparer.CurrentCulture).ToList())])];

        return itens
            .GroupBy(i => i.Ano is { } a ? $"{a - a % 10}–{a - a % 10 + 9}" : "sem data")
            .Select(g => new Decada(g.Key, g.Count(), g.GroupBy(i => i.Ano).Select(a => new Ano(a.Key, a.ToList())).ToList()))
            .ToList();
    }

    /// <summary>"Revista Exemplo — Tomo I, n.º 01" vira "Tomo I, n.º 01" dentro da própria coleção.</summary>
    public static string TituloCurto(string titulo, string colecao) =>
        titulo.StartsWith(colecao, StringComparison.CurrentCultureIgnoreCase) && titulo.Length > colecao.Length
            ? titulo[colecao.Length..].TrimStart(' ', '—', '–', '-', ':', ',')
            : titulo;
}
