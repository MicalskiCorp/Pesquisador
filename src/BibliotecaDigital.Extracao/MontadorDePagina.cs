using System.Text;
using System.Text.RegularExpressions;
using BibliotecaDigital.Acervo;

namespace BibliotecaDigital.Extracao;

/// <summary>Página montada, antes da conferência e da paginação impressa.</summary>
public sealed record PaginaMontada(
    IReadOnlyList<string> Margens,
    IReadOnlyList<Paragrafo> Paragrafos,
    int Colunas,
    IReadOnlyList<string> HifensRemovidos,
    int MeiaLargura);

/// <summary>
/// Transforma a geometria de uma página em texto legível — e só faz o que
/// <c>doc/formato-do-acervo.md</c> permite: ordem de leitura, parágrafos, hífen de fim de
/// linha, espaço, meia-largura e margem. Nenhuma letra é trocada; o OCR errado fica errado.
/// </summary>
public sealed partial class MontadorDePagina
{
    private readonly OpcoesDeExtracao _opcoes;
    private readonly Lexico? _lexico;
    private readonly Regex[] _margens;

    public MontadorDePagina(OpcoesDeExtracao opcoes, Lexico? lexico = null)
    {
        _opcoes = opcoes;
        _lexico = lexico;
        _margens = opcoes.Margens
            .Select(m => new Regex(m, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
    }

    public PaginaMontada Montar(PaginaLayout pagina)
    {
        var meiaLargura = 0;
        var blocos = new List<Bloco>();
        foreach (var b in pagina.Blocos)
        {
            var linhas = new List<Linha>();
            foreach (var l in b.Linhas)
            {
                var palavras = new List<Palavra>();
                foreach (var p in l.Palavras)
                {
                    var t = p.Texto;
                    if (_opcoes.DesfazerMeiaLargura)
                    {
                        t = Texto.DesfazerMeiaLargura(t, out var n);
                        meiaLargura += n;
                    }
                    t = t.Normalize(NormalizationForm.FormC).Trim();
                    if (t.Length > 0) palavras.Add(p with { Texto = Regex.Replace(t, @"\s+", " ") });
                }
                if (palavras.Count > 0) linhas.Add(l with { Palavras = palavras });
            }
            if (linhas.Count > 0) blocos.Add(b with { Linhas = linhas });
        }

        // A ordem de leitura é nossa (corte XY); do Poppler só vêm as palavras e as posições.
        var ordenados = OrdemDeLeitura.Blocos(pagina with { Blocos = blocos });
        var margens = new List<string>();
        var corpo = new List<Bloco>();
        foreach (var b in ordenados)
        {
            // Linha de margem colada ao corpo (o "-6-" logo abaixo do último parágrafo) sai pelas
            // pontas do bloco; o bloco que sobra ainda pode ser cabeçalho ou rodapé inteiro.
            var linhas = b.Linhas.ToList();
            var noFim = new List<string>();
            while (linhas.Count > 0 && LinhaDeMargem(linhas[0], pagina.Altura)) { margens.Add(linhas[0].Texto); linhas.RemoveAt(0); }
            while (linhas.Count > 0 && LinhaDeMargem(linhas[^1], pagina.Altura)) { noFim.Insert(0, linhas[^1].Texto); linhas.RemoveAt(linhas.Count - 1); }
            if (linhas.Count > 0)
            {
                var resto = linhas.Count == b.Linhas.Count ? b : b with { Linhas = linhas, Caixa = Envolver(linhas) };
                if (BlocoDeMargem(resto, pagina.Altura, pagina.Largura)) margens.Add(resto.Texto);
                else corpo.Add(resto);
            }
            margens.AddRange(noFim);
        }

        var (colunaDoBloco, colunas) = Colunas(corpo);
        var hifens = new List<string>();
        var paragrafos = MontarParagrafos(corpo, colunaDoBloco, hifens, _lexico);
        return new PaginaMontada(margens, paragrafos, colunas, hifens, meiaLargura);
    }

    // ---- margem -------------------------------------------------------------------------------

    private bool LinhaDeMargem(Linha l, double altura)
    {
        var t = l.Texto.Trim();
        if (_margens.Any(r => r.IsMatch(t))) return true;
        if (altura <= 0) return false;
        // Número de página isolado ("-8-", "— 8 —"), perto do pé ou do topo.
        if (SoNumero().IsMatch(t) && (l.Caixa.Y0 >= altura * 0.80 || l.Caixa.Y1 <= altura * 0.12)) return true;
        // Rodapé de citação com o número na ponta ("36 Rev. Exemplo, …, 2015").
        return l.Caixa.Y0 >= altura * 0.85 && NumeroNaPonta().IsMatch(t);
    }

    /// <summary>
    /// Cabeçalho ou rodapé corrido: uma linha sozinha, colada à borda, e curta ou estreita.
    /// Linha de corpo que abre a página também fica sozinha no topo ("0 prefeito Máfio
    /// Hildebrandt, em seu expressivo discurso durante a", 2024) — mas ocupa a coluna inteira.
    /// </summary>
    private static bool BlocoDeMargem(Bloco b, double altura, double largura) =>
        altura > 0 && b.Linhas.Count == 1
        && (b.Caixa.Y1 <= altura * 0.075 || b.Caixa.Y0 >= altura * 0.905)
        && (b.Texto.Length <= 40 || (b.Texto.Length <= 80 && b.Caixa.Largura < largura * 0.35));

    private static Caixa Envolver(IReadOnlyList<Linha> linhas) => new(
        linhas.Min(l => l.Caixa.X0), linhas.Min(l => l.Caixa.Y0), linhas.Max(l => l.Caixa.X1), linhas.Max(l => l.Caixa.Y1));

    // ---- colunas ------------------------------------------------------------------------------

    /// <summary>
    /// Agrupa os blocos estreitos e longos por sobreposição horizontal: cada grupo é uma coluna
    /// (ou uma das duas páginas impressas de uma digitalização em página dupla). Blocos largos —
    /// título, texto de coluna única — não formam coluna; herdam a que mais se sobrepõem.
    /// A ordem do texto não depende disto: ela é a do Poppler. Isto só alimenta o marcador.
    /// </summary>
    private static (Dictionary<Bloco, int>, int) Colunas(List<Bloco> corpo)
    {
        var resultado = new Dictionary<Bloco, int>(ReferenceEqualityComparer.Instance);
        if (corpo.Count == 0) return (resultado, 1);
        var largura = corpo.Max(b => b.Caixa.X1) - corpo.Min(b => b.Caixa.X0);
        var estreitos = corpo.Where(b => b.Linhas.Count >= 3 && b.Caixa.Largura < largura * 0.55).ToList();

        var grupos = new List<List<Bloco>>();
        foreach (var b in estreitos)
        {
            var junto = grupos.Where(g => g.Any(o => o.Caixa.SobreposicaoX(b.Caixa) >= 0.3 * Math.Min(o.Caixa.Largura, b.Caixa.Largura))).ToList();
            if (junto.Count == 0) { grupos.Add([b]); continue; }
            junto[0].Add(b);
            foreach (var outro in junto.Skip(1)) { junto[0].AddRange(outro); grupos.Remove(outro); }
        }
        var colunas = grupos
            .Where(g => g.Sum(b => b.Linhas.Count) >= 5)
            .Select(g => new Caixa(g.Min(b => b.Caixa.X0), 0, g.Max(b => b.Caixa.X1), 0))
            .OrderBy(c => c.X0)
            .ToList();

        foreach (var b in corpo)
        {
            if (colunas.Count < 2) { resultado[b] = 1; continue; }
            var melhor = Enumerable.Range(0, colunas.Count)
                .OrderByDescending(i => colunas[i].SobreposicaoX(b.Caixa))
                .ThenBy(i => Math.Abs(colunas[i].CentroX - b.Caixa.CentroX))
                .First();
            resultado[b] = melhor + 1;
        }
        return (resultado, Math.Max(1, colunas.Count));
    }

    // ---- parágrafos ---------------------------------------------------------------------------

    private static List<Paragrafo> MontarParagrafos(List<Bloco> corpo, Dictionary<Bloco, int> coluna, List<string> hifens, Lexico? lexico)
    {
        var paragrafos = new List<Paragrafo>();
        StringBuilder? atual = null;
        var colunaAtual = 1;

        void Fechar()
        {
            if (atual is null) return;
            var t = Espacos(atual.ToString());
            if (t.Length > 0) paragrafos.Add(new Paragrafo(t, colunaAtual));
            atual = null;
        }

        foreach (var bloco in corpo)
        {
            var linhas = bloco.Linhas;
            var esquerda = MargemEsquerda(linhas);
            var direita = linhas.Max(l => l.Caixa.X1);
            var caracteres = linhas.Sum(l => l.Texto.Length);
            var larguraDoCaractere = caracteres > 0 ? linhas.Sum(l => l.Caixa.Largura) / caracteres : 5;
            var recuo = Math.Max(4, 1.5 * larguraDoCaractere);

            for (var k = 0; k < linhas.Count; k++)
            {
                var linha = linhas[k];
                var texto = linha.Texto;
                var recuada = linhas.Count > 1 && linha.Caixa.X0 - esquerda >= recuo;
                bool novo;
                if (k == 0)
                {
                    // Fronteira de bloco: só continua o parágrafo anterior na mesma coluna, se a
                    // frase não tinha terminado e o bloco começa em minúscula. Mudança de coluna
                    // fecha o parágrafo: o marcador fica onde a coluna muda de fato, e a tela
                    // emenda o parágrafo que recomeça em minúscula.
                    novo = atual is null || recuada || coluna.GetValueOrDefault(bloco, 1) != colunaAtual
                        || TerminaFrase(atual.ToString()) || !ComecaMinuscula(texto);
                }
                else
                {
                    var anterior = linhas[k - 1];
                    var anteriorCurta = anterior.Caixa.X1 < direita - Math.Max(0.15 * (direita - esquerda), 6 * larguraDoCaractere)
                        && TerminaFrase(anterior.Texto);
                    novo = recuada || anteriorCurta;
                }

                if (novo)
                {
                    Fechar();
                    atual = new StringBuilder(texto);
                    colunaAtual = coluna.GetValueOrDefault(bloco, 1);
                }
                else Juntar(atual!, texto, hifens, lexico);
            }
        }
        Fechar();
        return paragrafos;
    }

    /// <summary>A margem esquerda mais comum do bloco (a moda), que é onde as linhas sem recuo começam.</summary>
    private static double MargemEsquerda(IReadOnlyList<Linha> linhas) =>
        linhas.GroupBy(l => Math.Round(l.Caixa.X0))
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .First().Min(l => l.Caixa.X0);

    // O ponto médio entra porque o OCR de 1966–1998 lê o hífen de fim de linha como "·".
    private const string Hifens = "-‐‑­¬·";

    /// <summary>Compostos que o léxico não reconhece sozinho (segunda-|feira).</summary>
    private static readonly HashSet<string> Compostos = new(StringComparer.OrdinalIgnoreCase) { "feira", "feiras" };

    private static readonly HashSet<string> Prefixos = new(StringComparer.OrdinalIgnoreCase)
    {
        "ex", "vice", "pós", "pré", "pró", "recém", "além", "aquém", "bem", "mal", "sub", "super", "anti",
    };

    /// <summary>
    /// O hífen de fim de linha é da palavra (fica) ou da hifenização (sai)? Sem léxico, sai —
    /// hifenização é de longe o caso mais comum no fim da linha.
    /// </summary>
    internal static bool ManterHifen(string antes, string depois, Lexico? lexico)
    {
        if (Compostos.Contains(depois) || Prefixos.Contains(antes)) return true;
        if (lexico is null || antes.Length == 0 || depois.Length == 0) return false;
        if (lexico.Contem(antes + depois)) return false;
        return lexico.Contem(antes) && lexico.Contem(depois);
    }

    internal static void Juntar(StringBuilder sb, string proxima, List<string> hifens, Lexico? lexico = null)
    {
        if (sb.Length == 0) { sb.Append(proxima); return; }
        var ultimo = sb[^1];
        var primeiraPalavra = PrimeiraPalavra(proxima);

        if (Hifens.Contains(ultimo) && sb.Length >= 2 && char.IsLetterOrDigit(sb[^2]))
        {
            if (char.IsLetter(sb[^2]) && proxima.Length > 0 && char.IsLower(proxima[0]))
            {
                var antes = UltimaPalavra(sb, 1);
                if (ManterHifen(antes, primeiraPalavra, lexico))
                {
                    sb.Append(proxima);
                    return;
                }
                hifens.Add(ultimo.ToString());
                sb.Length--;
                sb.Append(proxima);
                return;
            }
            if (proxima.Length > 0 && char.IsLetterOrDigit(proxima[0]))
            {
                sb.Append(proxima); // "Ale-|Manha", "1921-|1923": hífen fica, sem espaço
                return;
            }
        }

        // OCR que leu o hífen como ponto médio solto: "re ·|gião".
        if (sb.Length >= 3 && ultimo == '·' && sb[^2] == ' ' && char.IsLetter(sb[^3])
            && proxima.Length > 0 && char.IsLower(proxima[0]) && !ManterHifen(UltimaPalavra(sb, 2), primeiraPalavra, lexico))
        {
            hifens.Add("·");
            sb.Length -= 2;
            sb.Append(proxima);
            return;
        }

        sb.Append(' ').Append(proxima);
    }

    private static string PrimeiraPalavra(string s) =>
        new(s.TakeWhile(char.IsLetter).ToArray());

    private static string UltimaPalavra(StringBuilder sb, int pularNoFim)
    {
        var fim = sb.Length - pularNoFim;
        var inicio = fim;
        while (inicio > 0 && char.IsLetter(sb[inicio - 1])) inicio--;
        return sb.ToString(inicio, fim - inicio);
    }

    internal static bool TerminaFrase(string s) => FimDeFrase().IsMatch(s.TrimEnd());

    internal static bool ComecaMinuscula(string s)
    {
        foreach (var c in s)
        {
            if (char.IsLetter(c)) return char.IsLower(c);
            if (char.IsDigit(c)) return false;
        }
        return false;
    }

    /// <summary>Espaço é a única coisa que se normaliza: o multiconjunto de caracteres não muda.</summary>
    internal static string Espacos(string s)
    {
        s = EspacosRepetidos().Replace(s, " ").Trim();
        s = EspacoAntesDePontuacao().Replace(s, "$1");
        return EspacoDepoisDeAbertura().Replace(s, "$1");
    }

    [GeneratedRegex(@"^[^\p{L}\d]{0,4}\d{1,4}[^\p{L}\d]{0,4}$")]
    private static partial Regex SoNumero();

    [GeneratedRegex(@"^\d{1,4}\s+\p{L}|\p{L}.*\s\d{1,4}$")]
    private static partial Regex NumeroNaPonta();

    [GeneratedRegex(@"[.!?:;][""'»”’)\]]*$")]
    private static partial Regex FimDeFrase();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosRepetidos();

    [GeneratedRegex(@"\s+([.,;:!?)\]])")]
    private static partial Regex EspacoAntesDePontuacao();

    [GeneratedRegex(@"([(\[])\s+")]
    private static partial Regex EspacoDepoisDeAbertura();
}
