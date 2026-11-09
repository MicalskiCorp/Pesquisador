using System.Diagnostics;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.App;

/// <summary>
/// Os comandos de operação. Tudo o que a tela faz em lote também roda daqui, sem servidor:
/// é o caminho para a migração inicial e para recuperar o acervo numa máquina nova.
/// </summary>
public static class Cli
{
    private static readonly Dictionary<string, (string Ajuda, Func<Opcoes, Configuracao, Task<int>> Acao)> Comandos = new()
    {
        ["extrair"] = ("extrair <pdf> [--colecao id] [--saida arquivo.md]   mostra o texto que o extrator gera, sem gravar no acervo", Extrair),
        ["importar"] = ("importar <pasta-de-origem> --colecao id [--procedencia arquivo.tsv] [--limite N] [--refazer-lexico]\n      migração: importa <origem>/<ano>/*.pdf para o acervo", Importar),
        ["reextrair"] = ("reextrair [--todos | <id>]   refaz o texto a partir do PDF e da ficha (ficha preservada)", Reextrair),
        ["lexico"] = ("lexico   remonta acervo/lexico.txt a partir de todos os PDFs do acervo", RefazerLexico),
        ["auditar"] = ("auditar [--rapido]   confere ficha × PDF × texto × fidelidade de todo o acervo", Auditar),
        ["reindexar"] = ("reindexar   refaz o índice de busca (dados/busca.db) a partir do acervo", Reindexar),
        ["buscar"] = ("buscar \"termos\" [--modo exato|variacoes|tolerante] [--de ano] [--ate ano]   busca pela linha de comando", Buscar),
        ["artigos"] = ("artigos --de <pasta INDICE-ARTIGOS> [--substituir]   importa o catálogo de artigos dos editores para as fichas", Artigos),
        ["desfalques"] = ("desfalques   grava acervo/DESFALQUES.md: as edições que faltam, para entregar à instituição", GerarDesfalques),
        ["servico-instalar"] = ("servico-instalar   (administrador) inicia a Biblioteca Digital com o Windows e libera a porta na rede", InstalarServico),
        ["servico-remover"] = ("servico-remover   (administrador) desfaz o servico-instalar", RemoverServico),
    };

    private static Task<int> GerarDesfalques(Opcoes o, Configuracao cfg)
    {
        var repo = new RepositorioDoAcervo(cfg.Acervo);
        var docs = repo.Documentos().Select(d => Ficha.Ler(d.Ficha)).ToList();
        var relacao = repo.Colecoes().Where(c => c.Numeracao is not null)
            .Select(c => (c, docs.Count(f => f.Colecao == c.Id), Desfalques.Calcular(repo, c, DateTime.Now.Year)))
            .ToList();
        var arquivo = Path.Combine(cfg.Acervo, "DESFALQUES.md");
        ArquivoSeguro.GravarTexto(arquivo, Desfalques.Relatorio(relacao, DateTime.Now));
        foreach (var (c, _, lista) in relacao)
        {
            Console.WriteLine(c.Nome);
            foreach (var d in lista) Console.WriteLine($"  {d.Ano,-10} {d.Grupo,-7} n.º {d.Numero,-6} {d.Situacao}{(d.Observacao is null ? "" : " — " + d.Observacao)}");
        }
        Console.WriteLine($"{relacao.Sum(r => r.Item3.Count)} desfalques em {relacao.Count} coleção(ões) com numeração → {arquivo}");
        return Task.FromResult(0);
    }

    private const string NomeDoServico = "BibliotecaDigital";

    /// <summary>
    /// Serviço do Windows: a Biblioteca Digital sobe com a máquina, antes de qualquer login, e o endereço
    /// funciona para toda a rede do Arquivo. A regra de firewall abre só a porta configurada.
    /// </summary>
    private static Task<int> InstalarServico(Opcoes o, Configuracao cfg)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Serviço só no Windows. No Linux, use um serviço do systemd com 'biblioteca servir'.");
        var exe = Environment.ProcessPath!;
        var passos = new (string Programa, string Argumentos)[]
        {
            ("sc.exe", $"create {NomeDoServico} binPath= \"\\\"{exe}\\\" servir\" start= auto DisplayName= \"Biblioteca Digital\""),
            ("sc.exe", $"description {NomeDoServico} \"Busca no acervo. Endereço: http://<esta máquina>:{cfg.Porta}\""),
            ("sc.exe", $"failure {NomeDoServico} reset= 86400 actions= restart/5000/restart/5000/restart/60000"),
            ("netsh", $"advfirewall firewall add rule name=\"{NomeDoServico}\" dir=in action=allow protocol=TCP localport={cfg.Porta}"),
            ("sc.exe", $"start {NomeDoServico}"),
        };
        foreach (var (programa, argumentos) in passos)
            if (Rodar(programa, argumentos) != 0)
            {
                Console.Error.WriteLine("Falhou. Abra o terminal como administrador e rode de novo.");
                return Task.FromResult(1);
            }
        Console.WriteLine($"Pronto. A Biblioteca Digital inicia com o Windows. Nesta máquina: {cfg.EnderecoLocal} · na rede: http://{Environment.MachineName}:{cfg.Porta}/");
        return Task.FromResult(0);
    }

    private static Task<int> RemoverServico(Opcoes o, Configuracao cfg)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Serviço só no Windows.");
        Rodar("sc.exe", $"stop {NomeDoServico}");
        var r = Rodar("sc.exe", $"delete {NomeDoServico}");
        Rodar("netsh", $"advfirewall firewall delete rule name=\"{NomeDoServico}\"");
        return Task.FromResult(r);
    }

    private static int Rodar(string programa, string argumentos)
    {
        Console.WriteLine($"> {programa} {argumentos}");
        using var p = Process.Start(new ProcessStartInfo(programa, argumentos) { UseShellExecute = false })!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static Task<int> Reindexar(Opcoes o, Configuracao cfg)
    {
        var r = new BibliotecaDigital.Busca.Indexador(new RepositorioDoAcervo(cfg.Acervo))
            .ReconstruirTudo(cfg.Banco, (f, t) => Operacoes.Progresso("indexação", f, t));
        Console.Error.WriteLine();
        Console.WriteLine($"{r.Documentos} documentos · {r.Paginas:N0} páginas · {r.Termos:N0} termos · {r.Artigos:N0} artigos · {r.Duracao.TotalSeconds:0.0}s → {cfg.Banco}");
        foreach (var e in r.Erros) Console.WriteLine($"  ERRO {e}");
        return Task.FromResult(r.Erros.Count == 0 ? 0 : 2);
    }

    private static Task<int> Buscar(Opcoes o, Configuracao cfg)
    {
        var modo = o.Valor("--modo") switch { "variacoes" => BibliotecaDigital.Busca.Modo.Variacoes, "tolerante" => BibliotecaDigital.Busca.Modo.Tolerante, _ => BibliotecaDigital.Busca.Modo.Exato };
        var r = new BibliotecaDigital.Busca.MotorDeBusca(cfg.Banco).Buscar(new BibliotecaDigital.Busca.Pedido(
            o.Posicional(0) ?? throw new InvalidOperationException("informe os termos"), modo,
            AnoDe: o.Valor("--de") is { } de ? int.Parse(de) : null, AnoAte: o.Valor("--ate") is { } ate ? int.Parse(ate) : null, PorPagina: 10));
        if (r.Aviso is not null) Console.WriteLine(r.Aviso);
        Console.WriteLine($"{r.Documentos} documentos · {r.Paginas} páginas · {r.Milissegundos:0} ms");
        foreach (var v in r.Variantes) Console.WriteLine($"  variante: {v.Termo} → {v.Grafia} ({v.Ocorrencias})");
        foreach (var d in r.Itens)
        {
            Console.WriteLine();
            Console.WriteLine($"{d.Titulo}  [{d.Id}]");
            Console.WriteLine($"  páginas do PDF: {string.Join(", ", d.PaginasComOcorrencia.Take(30))}{(d.PaginasComOcorrencia.Count > 30 ? " …" : "")}");
            foreach (var p in d.Destaques)
            {
                Console.WriteLine($"  p. {p.Numero}{(p.Impressa is null ? "" : $" (impressa {p.Impressa})")}{string.Concat(p.Artigos.Select(a => $" — {a.Titulo}{(a.Autor is null ? "" : $", {a.Autor}")}"))}");
                foreach (var t in p.Trechos) Console.WriteLine("    " + string.Concat(t.Segmentos.Select(x => x.Destaque ? $"[{x.Texto}]" : x.Texto)));
            }
        }
        foreach (var a in r.Artigos) Console.WriteLine($"  artigo: {a.Titulo} — {a.Autor} ({a.TituloDoDocumento}, págs. {a.Paginas})");
        return Task.FromResult(0);
    }

    private static Task<int> Artigos(Opcoes o, Configuracao cfg) =>
        Task.FromResult(ImportacaoDeArtigos.Executar(new RepositorioDoAcervo(cfg.Acervo),
            o.Valor("--de") ?? throw new InvalidOperationException("informe --de <pasta INDICE-ARTIGOS>"), o.Tem("--substituir")));

    private static Task<int> Importar(Opcoes o, Configuracao cfg) =>
        Task.FromResult(Operacoes.ImportarArvore(cfg,
            o.Posicional(0) ?? throw new InvalidOperationException("informe a pasta de origem"),
            o.Valor("--colecao") ?? throw new InvalidOperationException("informe --colecao"),
            o.Valor("--procedencia"),
            o.Valor("--limite") is { } l ? int.Parse(l) : null,
            o.Tem("--refazer-lexico")));

    private static Task<int> Reextrair(Opcoes o, Configuracao cfg)
    {
        var repo = Operacoes.PrepararAcervo(cfg);
        var importador = new Importador(repo, new Extrator(new LeitorPoppler(cfg.Poppler), Operacoes.CarregarLexico(cfg)));
        var alvo = o.Tem("--todos")
            ? repo.Documentos().ToList()
            : [repo.Localizar(o.Posicional(0) ?? throw new InvalidOperationException("informe o id ou --todos"))
               ?? throw new KeyNotFoundException("documento não encontrado")];
        var feitos = 0; var div = 0;
        Parallel.ForEach(alvo, new ParallelOptions { MaxDegreeOfParallelism = Operacoes.Paralelismo }, d =>
        {
            var r = importador.Reextrair(d);
            Interlocked.Add(ref div, r.Divergencias.Count);
            Operacoes.Progresso("reextração", Interlocked.Increment(ref feitos), alvo.Count);
        });
        Console.Error.WriteLine();
        Console.WriteLine($"{alvo.Count} documentos reextraídos · {div} páginas divergentes");
        return Task.FromResult(0);
    }

    private static Task<int> RefazerLexico(Opcoes o, Configuracao cfg)
    {
        var repo = Operacoes.PrepararAcervo(cfg);
        // Cada PDF com as opções de extração da coleção dele.
        var colecoes = repo.Colecoes().ToDictionary(c => c.Id);
        var pdfs = repo.Documentos().Where(d => File.Exists(d.Pdf))
            .Select(d => (d.Pdf, colecoes.GetValueOrDefault(d.Colecao) ?? colecoes.Values.First())).ToList();
        var lexico = Operacoes.MontarLexico(cfg, pdfs);
        Console.WriteLine($"{lexico.Tamanho:N0} palavras em {Path.Combine(cfg.Acervo, Lexico.Arquivo)}. Rode 'reextrair --todos' para aplicar.");
        return Task.FromResult(0);
    }

    private static Task<int> Auditar(Opcoes o, Configuracao cfg)
    {
        var r = Auditoria.Auditar(new RepositorioDoAcervo(cfg.Acervo), conferirHash: !o.Tem("--rapido"));
        foreach (var grupo in r.Achados.GroupBy(a => a.Gravidade).OrderByDescending(g => g.Key))
        {
            Console.WriteLine($"\n{grupo.Key.ToString().ToUpperInvariant()} ({grupo.Count()})");
            foreach (var a in grupo.Take(40)) Console.WriteLine($"  {a.Documento}: {a.Mensagem}");
            if (grupo.Count() > 40) Console.WriteLine($"  … e mais {grupo.Count() - 40}");
        }
        Console.WriteLine($"\n{r.Documentos} documentos · {r.Paginas:N0} páginas · {r.Conferidas:N0} conferidas · {r.Divergentes} divergentes · {r.SemTexto} sem texto");
        Console.WriteLine(r.SemProblemas ? "SEM PROBLEMAS" : "HÁ PONTOS DE ATENÇÃO");
        return Task.FromResult(r.SemProblemas ? 0 : 3);
    }

    public static bool EhComando(string s) => Comandos.ContainsKey(s) || s is "ajuda" or "--help" or "-h";

    public static async Task<int> Executar(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (!Comandos.TryGetValue(args[0], out var cmd))
        {
            Console.WriteLine("""
                biblioteca                 abre a Biblioteca Digital no navegador (inicia o servidor se ainda não estiver no ar)
                biblioteca servir          só o servidor (é o que o serviço do Windows roda)
                biblioteca <comando>       operação:

                """);
            foreach (var (nome, c) in Comandos) Console.WriteLine($"  {c.Ajuda}");
            return 0;
        }
        try
        {
            return await cmd.Acao(new Opcoes(args.Skip(1).ToArray()), Configuracao.Carregar());
        }
        catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"erro: {e.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<Colecao> ColecoesConfiguradas(Configuracao cfg)
    {
        var noAcervo = Path.Combine(cfg.Acervo, RepositorioDoAcervo.ArquivoDeColecoes);
        var arquivo = File.Exists(noAcervo) ? noAcervo : Path.Combine(cfg.Config, RepositorioDoAcervo.ArquivoDeColecoes);
        return Colecoes.Ler(arquivo);
    }

    private static Task<int> Extrair(Opcoes o, Configuracao cfg)
    {
        var pdf = o.Posicional(0) ?? throw new InvalidOperationException("informe o PDF");
        var todas = ColecoesConfiguradas(cfg);
        var colecao = o.Valor("--colecao") is { } id
            ? todas.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException($"coleção '{id}' não existe")
            : todas.Count == 1 ? todas[0] : throw new InvalidOperationException("há mais de uma coleção: informe --colecao");
        var relogio = Stopwatch.StartNew();
        var extrator = new Extrator(new LeitorPoppler(cfg.Poppler));
        var nome = Path.GetFileName(pdf);
        var campos = colecao.SugerirCampos(nome);
        var ficha = new Ficha
        {
            Id = "previa/" + Texto.Slug(Path.GetFileNameWithoutExtension(nome)),
            Colecao = colecao.Id,
            Titulo = colecao.TituloDoDocumento(campos, nome),
            Campos = campos,
            Pdf = new DadosDoPdf { Arquivo = nome, Bytes = new FileInfo(pdf).Length, Sha256 = ArquivoSeguro.Sha256(pdf), Paginas = 0 },
        };
        var r = extrator.Extrair(pdf, colecao, ficha);
        var md = FormatoMarkdown.Gravar(r.Documento);
        if (o.Valor("--saida") is { } saida) ArquivoSeguro.GravarTexto(saida, md);
        else Console.WriteLine(md);
        Console.Error.WriteLine($"{r.Paginas} páginas · {r.PaginasConferidas} conferidas · {r.Divergencias.Count} divergentes · {r.PaginasSemTexto} sem texto · não lidos por página: máx {r.Documento.Paginas.Max(p => p.NaoLidos)}, total {r.Documento.Paginas.Sum(p => p.NaoLidos)} · {relogio.Elapsed.TotalSeconds:0.0}s");
        foreach (var d in r.Divergencias.Take(10))
            Console.Error.WriteLine($"  p. {d.Pagina}: faltando {Resumo(d.Faltando)} · sobrando {Resumo(d.Sobrando)}");
        return Task.FromResult(0);
    }

    internal static string Resumo(IReadOnlyDictionary<string, int> d) =>
        d.Count == 0 ? "—" : string.Join(" ", d.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"'{kv.Key}'×{kv.Value}"));

    /// <summary>Argumentos: posicionais e <c>--chave valor</c>.</summary>
    public sealed class Opcoes(string[] args)
    {
        public string? Valor(string chave)
        {
            var i = Array.IndexOf(args, chave);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public bool Tem(string chave) => args.Contains(chave);

        public string? Posicional(int n)
        {
            var pos = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--")) { if (i + 1 < args.Length && !args[i + 1].StartsWith("--") && !Bandeiras.Contains(args[i])) i++; continue; }
                pos.Add(args[i]);
            }
            return n < pos.Count ? pos[n] : null;
        }

        private static readonly HashSet<string> Bandeiras = ["--todos", "--forcar", "--vincular", "--json", "--seco", "--refazer-lexico", "--rapido", "--substituir"];
    }
}
