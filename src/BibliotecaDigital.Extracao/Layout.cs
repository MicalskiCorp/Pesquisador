namespace BibliotecaDigital.Extracao;

/// <summary>Retângulo em pontos, origem no canto superior esquerdo da página.</summary>
public readonly record struct Caixa(double X0, double Y0, double X1, double Y1)
{
    public double Largura => X1 - X0;
    public double Altura => Y1 - Y0;
    public double CentroX => (X0 + X1) / 2;

    public double SobreposicaoX(Caixa o) => Math.Max(0, Math.Min(X1, o.X1) - Math.Max(X0, o.X0));
    public double SobreposicaoY(Caixa o) => Math.Max(0, Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0));
}

public sealed record Palavra(string Texto, Caixa Caixa);

public sealed record Linha(IReadOnlyList<Palavra> Palavras, Caixa Caixa)
{
    public string Texto => string.Join(' ', Palavras.Select(p => p.Texto));
}

/// <summary>Bloco de texto contíguo, na ordem de leitura que o Poppler determinou.</summary>
public sealed record Bloco(int Fluxo, IReadOnlyList<Linha> Linhas, Caixa Caixa)
{
    public string Texto => string.Join(' ', Linhas.Select(l => l.Texto));
}

public sealed record PaginaLayout(int Numero, double Largura, double Altura, IReadOnlyList<Bloco> Blocos);
