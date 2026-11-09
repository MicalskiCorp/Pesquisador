using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BibliotecaDigital.Acervo;

/// <summary>Tipos de campo da ficha. Decidem a validação e o que se pode derivar (ano, mês, década).</summary>
public static class TiposDeCampo
{
    public const string Texto = "texto";
    public const string Numero = "numero";
    public const string Ano = "ano";
    /// <summary>Data como dd/mm/aaaa (aceita também mm/aaaa e aaaa).</summary>
    public const string Data = "data";
    public const string Opcoes = "opcoes";

    public static readonly string[] Todos = [Texto, Numero, Ano, Data, Opcoes];
}

/// <summary>Um campo de metadado que a coleção pede no cadastro (ano, tomo, número, data…).</summary>
public sealed record CampoDaColecao(string Chave, string Rotulo, bool Obrigatorio = false, string? Ajuda = null)
{
    public string Tipo { get; init; } = TiposDeCampo.Texto;
    /// <summary>Para o tipo "opcoes": os valores aceitos.</summary>
    public IReadOnlyList<string>? Opcoes { get; init; }
}

/// <summary>Opções de extração próprias da coleção.</summary>
public sealed record OpcoesDeExtracao
{
    /// <summary>Decodificar a corrupção meia-largura (ver <see cref="Texto.DesfazerMeiaLargura"/>).</summary>
    public bool DesfazerMeiaLargura { get; init; } = true;

    /// <summary>Expressões de linhas que são carimbo ou cabeçalho fixo: saem do corpo e vão para a margem.</summary>
    public IReadOnlyList<string> Margens { get; init; } = [];
}

/// <summary>Quantas edições por grupo (tomo, volume) até um ano. Sem <c>AteAno</c>: daí em diante.</summary>
public sealed record FaixaDeNumeracao(int Edicoes, int? AteAno = null);

/// <summary>
/// Numeração de um periódico, para saber que edições faltam: em cada grupo (o tomo da revista,
/// o volume de um jornal), os números de 1 a N. Coleção sem numeração não tem desfalques — um
/// livro ou documento avulso não tem "o próximo número".
/// </summary>
public sealed record NumeracaoDaColecao
{
    /// <summary>Campo do grupo (ex.: "tomo"). Romano ou arábico.</summary>
    public required string Grupo { get; init; }
    /// <summary>Campo do número dentro do grupo (ex.: "numero"); "11-12" cobre 11 e 12.</summary>
    public required string Numero { get; init; }
    public IReadOnlyList<FaixaDeNumeracao> Faixas { get; init; } = [new(12)];

    public int EdicoesPorGrupo(int? ano) =>
        Faixas.FirstOrDefault(f => f.AteAno is null || (ano is { } a && a <= f.AteAno))?.Edicoes ?? 12;
}

/// <summary>
/// Definição de uma coleção — um tipo de acervo —, lida de <c>acervo/colecoes.json</c>. É o que faz
/// o molde ser genérico: uma coleção nova é configuração, não código. Todo documento de toda
/// coleção passa pelo mesmo caminho: PDF com OCR → texto por página conferido → ficha → índice.
/// </summary>
public sealed record Colecao
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public string? Descricao { get; init; }

    /// <summary>
    /// Subpastas do documento, com campos entre chaves. Derivados: <c>{ano}</c> e <c>{decada}</c>
    /// (de um campo do tipo ano ou data) e <c>{mes}</c> (de um campo do tipo data). Vazio: sem subpastas.
    /// </summary>
    public string Pastas { get; init; } = "{ano}";

    /// <summary>Modelo do título de exibição, com campos entre chaves; <c>{colecao}</c> é o nome da coleção e <c>{arquivo}</c>, o do PDF.</summary>
    public string Titulo { get; init; } = "{arquivo}";

    public IReadOnlyList<CampoDaColecao> Campos { get; init; } = [];

    /// <summary>Expressões com grupos nomeados que sugerem campos a partir do nome do arquivo.</summary>
    public IReadOnlyList<string> NomesDeArquivo { get; init; } = [];

    public OpcoesDeExtracao Extracao { get; init; } = new();

    /// <summary>Numeração de periódico, para os desfalques. Opcional.</summary>
    public NumeracaoDaColecao? Numeracao { get; init; }

    /// <summary>
    /// O ano do documento: do campo "ano", senão do primeiro campo do tipo ano ou data. É o que
    /// organiza a busca por período e a navegação por década — em qualquer coleção datada.
    /// </summary>
    public int? Ano(IReadOnlyDictionary<string, string> campos)
    {
        foreach (var campo in CamposDeData())
            if (campos.TryGetValue(campo.Chave, out var v) && LerData(v, campo.Tipo) is { } d) return d.Ano;
        return campos.TryGetValue("ano", out var ano) && int.TryParse(ano, out var a) ? a : null;
    }

    private (int Ano, int? Mes)? Data(IReadOnlyDictionary<string, string> campos)
    {
        foreach (var campo in CamposDeData())
            if (campos.TryGetValue(campo.Chave, out var v) && LerData(v, campo.Tipo) is { } d) return d;
        return campos.TryGetValue("ano", out var ano) && int.TryParse(ano, out var a) ? (a, null) : null;
    }

    private IEnumerable<CampoDaColecao> CamposDeData() =>
        Campos.Where(c => c.Tipo is TiposDeCampo.Ano or TiposDeCampo.Data).OrderBy(c => c.Chave == "ano" ? 0 : 1);

    /// <summary>Lê "1988", "03/1988", "15/03/1988" ou "1988-03-15".</summary>
    public static (int Ano, int? Mes)? LerData(string? valor, string tipo = TiposDeCampo.Data)
    {
        valor = (valor ?? "").Trim();
        if (Regex.Match(valor, @"^(\d{4})$") is { Success: true } a) return (int.Parse(a.Value), null);
        if (tipo == TiposDeCampo.Ano) return null;
        if (Regex.Match(valor, @"^(?:\d{1,2}/)?(\d{1,2})/(\d{4})$") is { Success: true } br)
            return (int.Parse(br.Groups[2].Value), int.Parse(br.Groups[1].Value));
        if (Regex.Match(valor, @"^(\d{4})-(\d{1,2})(?:-\d{1,2})?$") is { Success: true } iso)
            return (int.Parse(iso.Groups[1].Value), int.Parse(iso.Groups[2].Value));
        return null;
    }

    /// <summary>Os campos que entram no caminho da pasta: depois de incluído, o documento não muda de lugar.</summary>
    public IReadOnlySet<string> CamposDaPasta()
    {
        var chaves = Regex.Matches(Pastas, @"\{(\w+)\}").Select(m => m.Groups[1].Value).ToHashSet();
        if (chaves.Overlaps(["ano", "decada", "mes"]))
            foreach (var c in CamposDeData()) chaves.Add(c.Chave);
        chaves.ExceptWith(["decada", "mes"]);
        // {ano} derivado de uma data não é campo da ficha.
        if (Campos.All(c => c.Chave != "ano")) chaves.Remove("ano");
        return chaves;
    }

    /// <summary>Monta o caminho relativo da pasta do documento (sem a coleção).</summary>
    public string PastaDoDocumento(IReadOnlyDictionary<string, string> campos) =>
        Substituir(Pastas, campos).Replace('\\', '/');

    public string TituloDoDocumento(IReadOnlyDictionary<string, string> campos, string arquivo)
    {
        var c = new Dictionary<string, string>(campos) { ["arquivo"] = Path.GetFileNameWithoutExtension(arquivo), ["colecao"] = Nome };
        var titulo = Substituir(Titulo, c);
        // Campo vazio deixa pontuação órfã ("n.º  ( 1966)"): limpar sem inventar valor.
        titulo = Regex.Replace(titulo, @"\s{2,}", " ");
        titulo = Regex.Replace(titulo, @"\(\s+", "(");
        titulo = Regex.Replace(titulo, @"\s+\)", ")");
        titulo = titulo.Replace("()", "");
        titulo = Regex.Replace(titulo, @",?\s*n\.º\s*(?=[(,]|$)", " ");
        titulo = Regex.Replace(titulo, @"\s+[—–-]\s*(?=[—–-]|$)", "");
        return Regex.Replace(titulo, @"\s{2,}", " ").Trim().TrimEnd(',', '—', ' ');
    }

    /// <summary>Sugere campos a partir do nome do arquivo. Não preenche o que não reconheceu.</summary>
    public Dictionary<string, string> SugerirCampos(string arquivo)
    {
        var nome = Path.GetFileNameWithoutExtension(arquivo);
        var sugestao = new Dictionary<string, string>();
        foreach (var padrao in NomesDeArquivo)
        {
            var m = Regex.Match(nome, padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) continue;
            foreach (var g in m.Groups.Values)
                if (g.Success && !int.TryParse(g.Name, out _) && g.Value.Trim().Length > 0)
                    sugestao[g.Name] = g.Value.Trim().Trim('-', ' ');
            break;
        }
        return sugestao;
    }

    /// <summary>O que está errado nos campos, um por linha. Vazio: pode gravar.</summary>
    public IReadOnlyList<string> ValidarCampos(IReadOnlyDictionary<string, string> campos)
    {
        var erros = new List<string>();
        foreach (var c in Campos)
        {
            var v = campos.GetValueOrDefault(c.Chave)?.Trim() ?? "";
            if (v.Length == 0)
            {
                if (c.Obrigatorio) erros.Add($"{c.Rotulo}: obrigatório.");
                continue;
            }
            switch (c.Tipo)
            {
                case TiposDeCampo.Ano when !(int.TryParse(v, out var a) && a is >= 1500 and <= 2200):
                    erros.Add($"{c.Rotulo}: informe o ano com quatro dígitos, como 1988."); break;
                case TiposDeCampo.Data when LerData(v) is null:
                    erros.Add($"{c.Rotulo}: informe a data como 15/03/1988, 03/1988 ou 1988."); break;
                case TiposDeCampo.Numero when !Regex.IsMatch(v, @"^\d+([.,]\d+)?$"):
                    erros.Add($"{c.Rotulo}: só números."); break;
                case TiposDeCampo.Opcoes when c.Opcoes is { Count: > 0 } ops && !ops.Contains(v, StringComparer.OrdinalIgnoreCase):
                    erros.Add($"{c.Rotulo}: escolha uma das opções ({string.Join(", ", ops)})."); break;
            }
        }
        if (CamposDaPasta().Count > 0 && Regex.IsMatch(Pastas, @"\{(ano|decada|mes)\}") && Ano(campos) is null)
            erros.Add("Esta coleção organiza as pastas pela data: informe o ano (ou a data) do documento.");
        if (Pastas.Contains("{mes}") && Data(campos) is { Mes: null })
            erros.Add("Esta coleção organiza as pastas por mês: informe a data com o mês.");
        return erros;
    }

    private string Substituir(string modelo, IReadOnlyDictionary<string, string> campos) =>
        Regex.Replace(modelo, @"\{(\w+)\}", m =>
        {
            var chave = m.Groups[1].Value;
            if (chave == "decada" && Ano(campos) is { } a) return $"{a - a % 10}-{a - a % 10 + 9}";
            if (chave == "ano" && !campos.ContainsKey("ano") && Ano(campos) is { } ano) return ano.ToString(CultureInfo.InvariantCulture);
            if (chave == "mes" && Data(campos) is { Mes: { } mes }) return mes.ToString("00", CultureInfo.InvariantCulture);
            return campos.TryGetValue(chave, out var v) ? v : "";
        });
}

/// <summary>Lê e grava <c>colecoes.json</c>.</summary>
public static class Colecoes
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IReadOnlyList<Colecao> Ler(string arquivo) =>
        JsonSerializer.Deserialize<List<Colecao>>(File.ReadAllText(arquivo), Json) ?? [];

    public static void Gravar(string arquivo, IReadOnlyList<Colecao> colecoes) =>
        ArquivoSeguro.GravarTexto(arquivo, JsonSerializer.Serialize(colecoes, Json) + "\n");

    private static readonly HashSet<string> Reservados = ["arquivo", "colecao", "decada", "mes"];

    /// <summary>
    /// O que impede gravar uma coleção cadastrada pela tela. Cada regra existe para que um tipo novo
    /// não quebre o acervo: identificador estável e único (vira pasta), campos que a pasta e o
    /// título citam existindo de fato, expressões que compilam, numeração apontando para campos reais.
    /// </summary>
    public static IReadOnlyList<string> Validar(Colecao c, IEnumerable<Colecao> outras)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(c.Nome)) erros.Add("Dê um nome à coleção.");
        if (!Regex.IsMatch(c.Id ?? "", "^[a-z0-9]+(-[a-z0-9]+)*$"))
            erros.Add("Identificador: só letras minúsculas sem acento, números e hífen (ex.: jornal-nsc).");
        if (outras.Any(o => o.Id == c.Id)) erros.Add($"Já existe uma coleção com o identificador “{c.Id}”.");

        var chaves = new HashSet<string>();
        foreach (var campo in c.Campos)
        {
            if (!Regex.IsMatch(campo.Chave, "^[a-z][a-z0-9_]*$"))
                erros.Add($"Campo “{campo.Rotulo}”: a chave “{campo.Chave}” deve ter só letras minúsculas sem acento, números e _.");
            if (Reservados.Contains(campo.Chave)) erros.Add($"Campo “{campo.Rotulo}”: “{campo.Chave}” é um nome reservado.");
            if (!chaves.Add(campo.Chave)) erros.Add($"Há dois campos com a chave “{campo.Chave}”.");
            if (string.IsNullOrWhiteSpace(campo.Rotulo)) erros.Add($"O campo “{campo.Chave}” precisa de um rótulo.");
            if (!TiposDeCampo.Todos.Contains(campo.Tipo)) erros.Add($"Campo “{campo.Rotulo}”: tipo “{campo.Tipo}” desconhecido.");
            if (campo.Tipo == TiposDeCampo.Opcoes && campo.Opcoes is not { Count: > 0 })
                erros.Add($"Campo “{campo.Rotulo}”: liste as opções aceitas.");
        }

        var temData = c.Campos.Any(x => x.Tipo is TiposDeCampo.Data or TiposDeCampo.Ano) || chaves.Contains("ano");
        foreach (Match m in Regex.Matches(c.Pastas ?? "", @"\{(\w+)\}"))
        {
            var k = m.Groups[1].Value;
            if (k is "ano" or "decada" && !temData) erros.Add($"A pasta usa {{{k}}}, mas a coleção não tem campo de ano ou de data.");
            else if (k == "mes" && c.Campos.All(x => x.Tipo != TiposDeCampo.Data)) erros.Add("A pasta usa {mes}, mas a coleção não tem campo de data.");
            else if (k is not ("ano" or "decada" or "mes") && !chaves.Contains(k)) erros.Add($"A pasta usa {{{k}}}, que não é um campo da coleção.");
        }
        foreach (Match m in Regex.Matches(c.Titulo ?? "", @"\{(\w+)\}"))
        {
            var k = m.Groups[1].Value;
            if (k is not ("arquivo" or "colecao" or "ano" or "decada" or "mes") && !chaves.Contains(k))
                erros.Add($"O título usa {{{k}}}, que não é um campo da coleção.");
        }
        foreach (var expressao in c.NomesDeArquivo.Concat(c.Extracao.Margens))
        {
            try { _ = new Regex(expressao); }
            catch (ArgumentException) { erros.Add($"Expressão inválida: “{expressao}”."); }
        }
        if (c.Numeracao is { } n)
        {
            if (string.IsNullOrWhiteSpace(n.Grupo)) erros.Add("Numeração: diga qual campo é o grupo (tomo, volume).");
            else if (!chaves.Contains(n.Grupo)) erros.Add($"Numeração: o grupo “{n.Grupo}” não é um campo da coleção.");
            if (string.IsNullOrWhiteSpace(n.Numero)) erros.Add("Numeração: diga qual campo é o número da edição.");
            else if (!chaves.Contains(n.Numero)) erros.Add($"Numeração: o número “{n.Numero}” não é um campo da coleção.");
            if (n.Faixas.Count == 0 || n.Faixas.Any(f => f.Edicoes < 1)) erros.Add("Numeração: informe quantas edições tem cada grupo.");
        }
        return erros;
    }
}
