namespace BibliotecaDigital.Extracao;

/// <summary>
/// Ordem de leitura por corte XY recursivo, a partir só das palavras e suas posições.
///
/// Não usa os blocos do Poppler porque eles falham em texto justificado com espaço largo: o
/// Poppler parte cada linha em dois blocos e intercala as metades (Tomo 56, 2015, p. 20). Aqui:
///
/// 1. <b>Corte vertical primeiro</b>, numa calha que atravesse a região inteira de cima a baixo,
///    com pelo menos três linhas de texto. Espaço de justificação não se alinha em todas as
///    linhas, então não vira coluna. Começar pelo vertical evita o erro clássico do corte XY:
///    uma linha em branco na mesma altura nas duas colunas intercalaria trechos delas.
/// 2. <b>Senão, corte horizontal</b> no maior vão entre linhas que seja maior que o
///    espaçamento normal — título sobre colunas, separação entre seções.
/// 3. <b>Senão, folha</b>: palavras agrupadas em linhas por sobreposição vertical, linhas de
///    cima para baixo, palavras da esquerda para a direita.
/// </summary>
public static class OrdemDeLeitura
{
    public static IReadOnlyList<Bloco> Blocos(PaginaLayout pagina)
    {
        // Todas as palavras entram, inclusive as de caixa degenerada: a conferência de
        // fidelidade exige que nenhum caractere lido fique de fora.
        var palavras = pagina.Blocos.SelectMany(b => b.Linhas).SelectMany(l => l.Palavras).ToList();
        if (palavras.Count == 0) return [];
        var alturaTipica = Mediana(palavras.Select(p => p.Caixa.Altura).Where(a => a > 0));
        var saida = new List<Bloco>();
        Cortar(palavras, alturaTipica, saida, 0);
        return saida;
    }

    private static void Cortar(List<Palavra> palavras, double alturaTipica, List<Bloco> saida, int profundidade)
    {
        if (profundidade < 40 && palavras.Count > 1)
        {
            if (CalhaVertical(palavras, alturaTipica) is { } calha)
            {
                if (calha.Atravessam.Count == 0)
                {
                    Cortar(palavras.Where(p => p.Caixa.CentroX < calha.X).ToList(), alturaTipica, saida, profundidade + 1);
                    Cortar(palavras.Where(p => p.Caixa.CentroX >= calha.X).ToList(), alturaTipica, saida, profundidade + 1);
                    return;
                }
                // Título, rodapé ou carimbo atravessando a calha: corta-se acima e abaixo dessas
                // linhas, e cada faixa que sobra se divide em colunas na volta da recursão.
                var faixas = FaixasEntre(palavras, calha.Atravessam);
                if (faixas.Count > 1)
                {
                    foreach (var f in faixas) Cortar(f, alturaTipica, saida, profundidade + 1);
                    return;
                }
            }
            var horizontal = VaoHorizontal(palavras, alturaTipica);
            if (horizontal is { } y)
            {
                Cortar(palavras.Where(p => (p.Caixa.Y0 + p.Caixa.Y1) / 2 < y).ToList(), alturaTipica, saida, profundidade + 1);
                Cortar(palavras.Where(p => (p.Caixa.Y0 + p.Caixa.Y1) / 2 >= y).ToList(), alturaTipica, saida, profundidade + 1);
                return;
            }
        }
        if (palavras.Count > 0) saida.Add(Folha(palavras));
    }

    private sealed record Calha(double X, List<List<Palavra>> Atravessam);

    /// <summary>
    /// A calha entre colunas: uma faixa vertical que quase nenhuma linha atravessa. Tolera até
    /// 25% de linhas atravessando (o "-8-" e o carimbo centralizados no pé, um título largo),
    /// desde que haja ao menos três linhas de texto de cada lado. Região pequena (menos de seis
    /// linhas) só se corta com calha limpa e larga — três linhas justificadas podem ter um vão
    /// alinhado por coincidência.
    /// </summary>
    private static Calha? CalhaVertical(List<Palavra> palavras, double alturaTipica)
    {
        var linhas = AgruparEmLinhas(palavras);
        if (linhas.Count < 3) return null;
        var pequena = linhas.Count < 6;
        var minimo = Math.Max(6, (pequena ? 1.5 : 0.9) * alturaTipica);
        var permitidas = pequena ? 0 : (int)Math.Floor(linhas.Count * 0.25);

        var x0 = (int)Math.Floor(palavras.Min(p => p.Caixa.X0));
        var x1 = (int)Math.Ceiling(palavras.Max(p => p.Caixa.X1));
        if (x1 - x0 < 3 * minimo) return null;
        var cobertura = new int[x1 - x0 + 1];
        foreach (var linha in linhas)
        {
            var marcado = new bool[cobertura.Length];
            foreach (var p in linha)
                for (var x = Math.Max(x0, (int)Math.Floor(p.Caixa.X0)); x <= Math.Min(x1, (int)Math.Ceiling(p.Caixa.X1)); x++)
                    marcado[x - x0] = true;
            for (var i = 0; i < marcado.Length; i++) if (marcado[i]) cobertura[i]++;
        }

        Calha? melhor = null;
        var larguraMelhor = 0.0;
        var inicio = -1;
        for (var i = 0; i <= cobertura.Length; i++)
        {
            var livre = i < cobertura.Length && cobertura[i] <= permitidas;
            if (livre && inicio < 0) inicio = i;
            if (livre || inicio < 0) continue;
            var (ia, ib) = (inicio, i - 1);
            var (a, b) = (x0 + ia, x0 + ib);
            inicio = -1;
            if (a <= x0 || b >= x1 || b - a + 1 < minimo || b - a + 1 <= larguraMelhor) continue;
            // O corte vai no trecho de menor cobertura da calha. Quem só encosta na borda dela
            // (um hífen que avança, o fim de "re·") não atravessa; o rodapé centralizado sim.
            var menor = Enumerable.Range(ia, ib - ia + 1).Min(k => cobertura[k]);
            int melhorIni = ia, melhorTam = 0, atualIni = -1;
            for (var k = ia; k <= ib + 1; k++)
            {
                if (k <= ib && cobertura[k] == menor) { if (atualIni < 0) atualIni = k; continue; }
                if (atualIni >= 0 && k - atualIni > melhorTam) { melhorIni = atualIni; melhorTam = k - atualIni; }
                atualIni = -1;
            }
            var corte = x0 + melhorIni + (melhorTam - 1) / 2.0;
            var atravessam = linhas.Where(l => l.Any(p => p.Caixa.X0 < corte && p.Caixa.X1 > corte)).ToList();
            var resto = linhas.Except(atravessam).ToList();
            var esquerda = resto.Count(l => l.Any(p => p.Caixa.CentroX < corte));
            var direita = resto.Count(l => l.Any(p => p.Caixa.CentroX >= corte));
            if (esquerda < 3 || direita < 3) continue;
            melhor = new Calha(corte, atravessam);
            larguraMelhor = b - a + 1;
        }
        return melhor;
    }

    /// <summary>Divide a região em faixas horizontais: antes, a própria linha, e depois de cada linha que atravessa.</summary>
    private static List<List<Palavra>> FaixasEntre(List<Palavra> palavras, List<List<Palavra>> atravessam)
    {
        var limites = atravessam.Select(l => (Y0: l.Min(p => p.Caixa.Y0), Y1: l.Max(p => p.Caixa.Y1))).OrderBy(l => l.Y0).ToList();
        var faixas = new List<List<Palavra>>();
        var restantes = palavras.OrderBy(p => (p.Caixa.Y0 + p.Caixa.Y1) / 2).ToList();
        foreach (var (y0, y1) in limites)
        {
            var antes = restantes.Where(p => (p.Caixa.Y0 + p.Caixa.Y1) / 2 < y0).ToList();
            var dentro = restantes.Where(p => { var c = (p.Caixa.Y0 + p.Caixa.Y1) / 2; return c >= y0 && c <= y1; }).ToList();
            if (antes.Count > 0) faixas.Add(antes);
            if (dentro.Count > 0) faixas.Add(dentro);
            restantes = restantes.Except(antes).Except(dentro).ToList();
        }
        if (restantes.Count > 0) faixas.Add(restantes);
        return faixas;
    }

    /// <summary>O maior vão vertical entre linhas, se for maior que o espaçamento normal.</summary>
    private static double? VaoHorizontal(List<Palavra> palavras, double alturaTipica)
    {
        var intervalos = palavras.Select(p => (p.Caixa.Y0, p.Caixa.Y1)).OrderBy(i => i.Y0).ToList();
        double? melhor = null; double maior = 0;
        var fim = intervalos[0].Y1;
        foreach (var (y0, y1) in intervalos.Skip(1))
        {
            var vao = y0 - fim;
            if (vao >= 0.9 * alturaTipica && vao > maior) { maior = vao; melhor = (fim + y0) / 2; }
            fim = Math.Max(fim, y1);
        }
        return melhor;
    }

    private static Bloco Folha(List<Palavra> palavras)
    {
        var linhas = AgruparEmLinhas(palavras)
            .Select(ps =>
            {
                var ordenadas = Unir(ps.OrderBy(p => p.Caixa.X0).ToList());
                return new Linha(ordenadas, Envolver(ordenadas.Select(p => p.Caixa)));
            })
            .ToList();
        return new Bloco(0, linhas, Envolver(linhas.Select(l => l.Caixa)));
    }

    /// <summary>
    /// Junta palavras que o Poppler separou dentro de uma palavra só ("T|i|vemos", "NOV|A"):
    /// vão menor que 0,12 da altura da letra. Medido em 1960, p. 3: dentro da palavra o vão
    /// fica entre 0,05 e 0,10; entre palavras, só 2% dos vãos ficam abaixo de 0,13. Título com
    /// letra espaçada de propósito ("I I I", 0,24) continua como foi impresso.
    /// </summary>
    internal static List<Palavra> Unir(List<Palavra> ordenadas)
    {
        var saida = new List<Palavra>(ordenadas.Count);
        foreach (var p in ordenadas)
        {
            if (saida.Count > 0)
            {
                var a = saida[^1];
                var altura = Math.Max(a.Caixa.Altura, p.Caixa.Altura);
                var vao = p.Caixa.X0 - a.Caixa.X1;
                if (altura > 0 && vao < 0.12 * altura && vao > -0.5 * altura)
                {
                    saida[^1] = new Palavra(a.Texto + p.Texto, Envolver([a.Caixa, p.Caixa]));
                    continue;
                }
            }
            saida.Add(p);
        }
        return saida;
    }

    /// <summary>Agrupa palavras em linhas: entra na linha quem se sobrepõe verticalmente a ela em mais da metade.</summary>
    internal static List<List<Palavra>> AgruparEmLinhas(List<Palavra> palavras)
    {
        var linhas = new List<(double Y0, double Y1, List<Palavra> Palavras)>();
        foreach (var p in palavras.OrderBy(p => (p.Caixa.Y0 + p.Caixa.Y1) / 2))
        {
            var achou = false;
            for (var i = linhas.Count - 1; i >= 0 && i >= linhas.Count - 3; i--)
            {
                var (y0, y1, lista) = linhas[i];
                var sobre = Math.Min(y1, p.Caixa.Y1) - Math.Max(y0, p.Caixa.Y0);
                if (sobre >= 0.5 * Math.Min(y1 - y0, p.Caixa.Altura))
                {
                    lista.Add(p);
                    linhas[i] = (Math.Min(y0, p.Caixa.Y0), Math.Max(y1, p.Caixa.Y1), lista);
                    achou = true;
                    break;
                }
            }
            if (!achou) linhas.Add((p.Caixa.Y0, p.Caixa.Y1, [p]));
        }
        return linhas.Select(l => l.Palavras).ToList();
    }

    private static Caixa Envolver(IEnumerable<Caixa> caixas)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var c in caixas) { x0 = Math.Min(x0, c.X0); y0 = Math.Min(y0, c.Y0); x1 = Math.Max(x1, c.X1); y1 = Math.Max(y1, c.Y1); }
        return new Caixa(x0, y0, x1, y1);
    }

    private static double Mediana(IEnumerable<double> valores)
    {
        var v = valores.OrderBy(x => x).ToList();
        return v.Count == 0 ? 10 : v[v.Count / 2];
    }
}
