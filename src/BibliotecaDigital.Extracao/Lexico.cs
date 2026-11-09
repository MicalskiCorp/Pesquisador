using System.Globalization;
using System.Text;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Extracao;

/// <summary>
/// As palavras que o próprio acervo escreve inteiras. Decide o hífen de fim de linha: se
/// "parte" aparece inteira em outros lugares, "par-|te" é hifenização e o hífen sai; se
/// "tornouse" nunca aparece mas "tornou" e "se" sim, o hífen é da palavra e fica.
///
/// Vem do acervo e não de um dicionário porque o acervo tem a ortografia de época (êste,
/// côr, Destêrro) que nenhum dicionário atual tem. Fica gravado em <c>acervo/lexico.txt</c>
/// para que a extração seja reproduzível: mesmo léxico, mesmo texto.
/// </summary>
public sealed class Lexico
{
    public const string Arquivo = "lexico.txt";
    public const int OcorrenciasMinimas = 2;

    private readonly HashSet<string> _palavras;

    private Lexico(HashSet<string> palavras) => _palavras = palavras;

    public int Tamanho => _palavras.Count;

    public bool Contem(string palavra) => _palavras.Contains(palavra.ToLowerInvariant());

    public static Lexico? Carregar(string arquivo)
    {
        if (!File.Exists(arquivo)) return null;
        var palavras = new HashSet<string>(StringComparer.Ordinal);
        foreach (var linha in File.ReadLines(arquivo))
        {
            var tab = linha.IndexOf('\t');
            var p = tab > 0 ? linha[..tab] : linha;
            if (p.Length > 0) palavras.Add(p);
        }
        return new Lexico(palavras);
    }

    public static Lexico DeContagem(IReadOnlyDictionary<string, int> contagem) =>
        new(contagem.Where(kv => kv.Value >= OcorrenciasMinimas).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal));

    public static void Gravar(string arquivo, IReadOnlyDictionary<string, int> contagem)
    {
        var sb = new StringBuilder();
        foreach (var (p, n) in contagem.Where(kv => kv.Value >= OcorrenciasMinimas).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(p).Append('\t').Append(n.ToString(CultureInfo.InvariantCulture)).Append('\n');
        ArquivoSeguro.GravarTexto(arquivo, sb.ToString());
    }
}

/// <summary>
/// Conta as palavras que aparecem inteiras numa linha do PDF. Exclui o pedaço antes de um
/// hífen de fim de linha e o pedaço que começa a linha seguinte: contá-los ensinaria ao
/// léxico exatamente as palavras partidas que ele existe para resolver.
/// </summary>
public sealed class ColetorDeLexico(bool desfazerMeiaLarguraPadrao = true)
{
    public Dictionary<string, int> Contagem { get; } = new(StringComparer.Ordinal);

    public void Contar(IEnumerable<PaginaLayout> paginas, bool? desfazerMeiaLargura = null)
    {
        var desfazer = desfazerMeiaLargura ?? desfazerMeiaLarguraPadrao;
        foreach (var pagina in paginas)
            foreach (var bloco in pagina.Blocos)
            {
                var anteriorPartida = false;
                foreach (var linha in bloco.Linhas)
                {
                    var t = string.Join(' ', linha.Palavras.Select(p => p.Texto));
                    if (desfazer) t = Texto.DesfazerMeiaLargura(t, out _);
                    t = t.Normalize(NormalizationForm.FormC);
                    var tokens = Tokens(t);
                    var partida = t.Length > 1 && "-‐‑­¬".Contains(t[^1]) && char.IsLetter(t[^2]);
                    for (var i = 0; i < tokens.Count; i++)
                    {
                        if (i == 0 && anteriorPartida) continue;
                        if (i == tokens.Count - 1 && partida) continue;
                        if (tokens[i].Length < 2) continue;
                        Contagem[tokens[i]] = Contagem.GetValueOrDefault(tokens[i]) + 1;
                    }
                    anteriorPartida = partida;
                }
            }
    }

    /// <summary>Sequências de letras, em minúsculas. Hífen e apóstrofo separam.</summary>
    public static List<string> Tokens(string s)
    {
        var lista = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0) { lista.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) lista.Add(sb.ToString());
        return lista;
    }
}
