using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BibliotecaDigital.Acervo;

/// <summary>
/// Grava e lê o <c>.md</c> de um documento — o molde permanente do acervo, especificado em
/// <c>doc/formato-do-acervo.md</c>. Ida e volta é exata: ler o que foi gravado devolve as
/// mesmas páginas, parágrafos e margens, caractere por caractere.
/// </summary>
public static partial class FormatoMarkdown
{
    // O "pesquisador" é do nome antigo do projeto e fica: este identificador está gravado em
    // cada .md do acervo. Trocá-lo é mudar de formato (nova versão + reextração), não renomear.
    public const string Versao = "pesquisador/1";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Gravar(DocumentoTexto doc)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        foreach (var (chave, valor) in doc.Cabecalho)
            sb.Append(chave).Append(": ").Append(JsonSerializer.Serialize(valor, Json)).Append('\n');
        sb.Append("---\n\n");
        sb.Append("# ").Append(EscaparParagrafo(doc.Titulo)).Append("\n\n");

        foreach (var p in doc.Paginas)
        {
            sb.Append("<!-- p. ").Append(p.Numero).Append(" -->\n");
            sb.Append("## Página ").Append(p.Numero);
            if (p.Impressa is not null)
                sb.Append(" · impressa ").Append(p.Impressa);
            sb.Append('\n');
            sb.Append("<!-- pagina ").Append(AtributosDaPagina(p)).Append(" -->\n");
            foreach (var m in p.Margens)
                sb.Append("<!-- margem: ").Append(EscaparComentario(m)).Append(" -->\n");
            sb.Append('\n');

            var coluna = 1;
            foreach (var par in p.Paragrafos)
            {
                if (p.Colunas > 1 && par.Coluna != coluna)
                {
                    coluna = par.Coluna;
                    sb.Append("<!-- coluna ").Append(coluna).Append(" -->\n\n");
                }
                sb.Append(EscaparParagrafo(par.Texto)).Append("\n\n");
            }
        }
        return sb.ToString();
    }

    private static string AtributosDaPagina(PaginaTexto p)
    {
        var a = new List<string> { $"pdf={p.Numero}" };
        if (p.Impressa is not null)
        {
            a.Add($"impressa={p.Impressa}");
            if (p.OrigemImpressa is { } o) a.Add($"impressa_origem={Nome(o)}");
        }
        if (p.Colunas > 1) a.Add($"colunas={p.Colunas}");
        a.Add($"fidelidade={Nome(p.Fidelidade)}");
        if (p.HifensRemovidos > 0) a.Add($"hifens={p.HifensRemovidos}");
        if (p.MeiaLargura > 0) a.Add($"meia_largura={p.MeiaLargura}");
        if (p.NaoLidos > 0) a.Add($"nao_lidos={p.NaoLidos}");
        return string.Join(' ', a);
    }

    public static DocumentoTexto Ler(string conteudo)
    {
        var linhas = conteudo.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        var cabecalho = new List<KeyValuePair<string, string>>();
        if (linhas.Length > 0 && linhas[0] == "---")
        {
            for (i = 1; i < linhas.Length && linhas[i] != "---"; i++)
            {
                var dois = linhas[i].IndexOf(':');
                if (dois <= 0) continue;
                var chave = linhas[i][..dois].Trim();
                var bruto = linhas[i][(dois + 1)..].Trim();
                var valor = bruto.StartsWith('"') ? JsonSerializer.Deserialize<string>(bruto, Json)! : bruto;
                cabecalho.Add(new(chave, valor));
            }
            i++;
        }

        var titulo = "";
        var paginas = new List<PaginaTexto>();
        PaginaEmLeitura? atual = null;
        for (; i < linhas.Length; i++)
        {
            var l = linhas[i];
            if (l.Length == 0) continue;

            var marcador = MarcadorDePagina().Match(l);
            if (marcador.Success)
            {
                if (atual is not null) paginas.Add(atual.Fechar());
                atual = new PaginaEmLeitura(int.Parse(marcador.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }
            if (atual is null)
            {
                if (l.StartsWith("# ")) titulo = DesescaparParagrafo(l[2..]);
                continue;
            }
            if (l.StartsWith("## ")) continue;
            if (l.StartsWith("<!-- pagina ") && l.EndsWith(" -->"))
            {
                foreach (var par in l["<!-- pagina ".Length..^" -->".Length].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var igual = par.IndexOf('=');
                    if (igual > 0) atual.Atributos[par[..igual]] = par[(igual + 1)..];
                }
                continue;
            }
            if (l.StartsWith("<!-- margem: ") && l.EndsWith(" -->"))
            {
                atual.Margens.Add(DesescaparComentario(l["<!-- margem: ".Length..^" -->".Length]));
                continue;
            }
            var col = MarcadorDeColuna().Match(l);
            if (col.Success) { atual.Coluna = int.Parse(col.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            if (l.StartsWith("<!--")) continue; // comentário de anotação: não é texto do PDF

            atual.Paragrafos.Add(new Paragrafo(DesescaparParagrafo(l), atual.Coluna));
        }
        if (atual is not null) paginas.Add(atual.Fechar());

        return new DocumentoTexto { Cabecalho = cabecalho, Titulo = titulo, Paginas = paginas };
    }

    private sealed class PaginaEmLeitura(int numero)
    {
        public Dictionary<string, string> Atributos { get; } = new();
        public List<string> Margens { get; } = [];
        public List<Paragrafo> Paragrafos { get; } = [];
        public int Coluna { get; set; } = 1;

        public PaginaTexto Fechar()
        {
            string? A(string k) => Atributos.GetValueOrDefault(k);
            int N(string k) => int.TryParse(A(k), out var v) ? v : 0;
            return new PaginaTexto
            {
                Numero = numero,
                Impressa = A("impressa"),
                OrigemImpressa = A("impressa_origem") is { } o ? Enum.Parse<OrigemImpressa>(o, true) : null,
                Colunas = Math.Max(1, N("colunas")),
                Fidelidade = A("fidelidade") switch
                {
                    "divergente" => Fidelidade.Divergente,
                    "sem_texto" => Fidelidade.SemTexto,
                    _ => Fidelidade.Conferida,
                },
                HifensRemovidos = N("hifens"),
                MeiaLargura = N("meia_largura"),
                NaoLidos = N("nao_lidos"),
                Margens = Margens,
                Paragrafos = Paragrafos,
            };
        }
    }

    public static string Nome(Fidelidade f) => f switch
    {
        Fidelidade.Conferida => "conferida",
        Fidelidade.Divergente => "divergente",
        _ => "sem_texto",
    };

    private static string Nome(OrigemImpressa o) => o.ToString().ToLowerInvariant();

    // ---- escape ---------------------------------------------------------------------------
    // O texto vem de OCR e pode ter qualquer caractere. Sem escape, um "#" no começo da linha
    // vira título, "~~" vira tachado e "<" abre HTML — a tela mostraria outra coisa que o PDF.

    private const string EscapaveisEmQualquerLugar = "\\`*_[]<>~&|";

    public static string EscaparParagrafo(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (EscapaveisEmQualquerLugar.Contains(c) || (i == 0 && "#+-=".Contains(c)))
                sb.Append('\\');
            sb.Append(c);
        }
        // "1. texto" e "1) texto" no começo viram lista numerada: escapar a pontuação.
        var lista = InicioDeLista().Match(sb.ToString());
        if (lista.Success)
            sb.Insert(lista.Groups[1].Length, '\\');
        return sb.ToString();
    }

    public static string DesescaparParagrafo(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && char.IsAsciiLetterOrDigit(s[i + 1]) is false && s[i + 1] < 128)
                i++;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string EscaparComentario(string s) => s.Replace("\\", "\\\\").Replace(">", "\\>");

    private static string DesescaparComentario(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && (s[i + 1] == '\\' || s[i + 1] == '>')) i++;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^<!-- p\. (\d+) -->\s*$")]
    private static partial Regex MarcadorDePagina();

    [GeneratedRegex(@"^<!-- coluna (\d+) -->$")]
    private static partial Regex MarcadorDeColuna();

    [GeneratedRegex(@"^(\d{1,9})[.)]")]
    private static partial Regex InicioDeLista();
}
