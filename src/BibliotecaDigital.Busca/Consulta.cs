using System.Text;

namespace BibliotecaDigital.Busca;

public enum Modo { Exato, Variacoes, Tolerante }

/// <summary>Um termo da consulta: palavra, frase entre aspas, prefixo (casa*) ou exclusão (-casa).</summary>
public sealed record Termo(IReadOnlyList<string> Palavras, bool Frase, bool Prefixo, bool Negado)
{
    public string Exibicao => (Negado ? "-" : "") + (Frase ? $"\"{string.Join(' ', Palavras)}\"" : string.Join(' ', Palavras)) + (Prefixo ? "*" : "");
}

/// <summary>
/// Lê o que o biblioteca digitou. A sintaxe é a de qualquer buscador: palavras soltas (todas
/// precisam aparecer na página), "frase entre aspas", prefixo com asterisco e exclusão com
/// hífen. Nada do que é digitado chega cru ao FTS5 — cada palavra é citada, então pontuação
/// de OCR ou de época nunca vira operador por acidente.
/// </summary>
public static class Consulta
{
    public static List<Termo> Interpretar(string texto)
    {
        var termos = new List<Termo>();
        var i = 0;
        while (i < texto.Length)
        {
            while (i < texto.Length && char.IsWhiteSpace(texto[i])) i++;
            if (i >= texto.Length) break;
            var negado = false;
            if (texto[i] == '-' && i + 1 < texto.Length && !char.IsWhiteSpace(texto[i + 1])) { negado = true; i++; }

            string trecho; var frase = false;
            if (i < texto.Length && texto[i] is '"' or '“' or '”')
            {
                var fim = texto.IndexOfAny(['"', '“', '”'], i + 1);
                if (fim < 0) fim = texto.Length;
                trecho = texto[(i + 1)..fim];
                i = Math.Min(texto.Length, fim + 1);
                frase = true;
            }
            else
            {
                var ini = i;
                while (i < texto.Length && !char.IsWhiteSpace(texto[i])) i++;
                trecho = texto[ini..i];
            }

            var prefixo = !frase && trecho.EndsWith('*');
            var palavras = Tokens.Dobrados(trecho.TrimEnd('*')).ToList();
            if (palavras.Count == 0) continue;
            // "15-11" ou "d'Ávila" viram mais de uma palavra: ficam juntas, como frase.
            termos.Add(new Termo(palavras, frase || palavras.Count > 1, prefixo && palavras.Count == 1, negado));
        }
        return termos;
    }

    /// <summary>Monta a expressão FTS5. Cada palavra vai entre aspas; nada do usuário vira operador.</summary>
    public static string? ParaFts(IReadOnlyList<Termo> termos, Func<Termo, string?> expressao)
    {
        var positivos = termos.Where(t => !t.Negado).Select(expressao).Where(e => e is not null).ToList();
        if (positivos.Count == 0) return null;
        var sb = new StringBuilder(string.Join(" AND ", positivos));
        foreach (var n in termos.Where(t => t.Negado).Select(expressao).Where(e => e is not null))
            sb.Append(" NOT ").Append(n);
        return sb.ToString();
    }

    public static string Citar(string palavra) => "\"" + palavra.Replace("\"", "\"\"") + "\"";

    public static string Exata(Termo t) =>
        t.Frase ? Citar(string.Join(' ', t.Palavras)) : Citar(t.Palavras[0]) + (t.Prefixo ? "*" : "");
}
