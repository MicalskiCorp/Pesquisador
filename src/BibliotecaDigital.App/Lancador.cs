using System.Diagnostics;
using System.Net.Sockets;

namespace BibliotecaDigital.App;

/// <summary>
/// O que acontece no duplo-clique: se a Biblioteca Digital já está no ar nesta máquina, só abre o
/// navegador; se não está, sobe o servidor em segundo plano (sem janela) e então abre. Quem
/// consulta pela rede só precisa do endereço — o servidor fica no ar enquanto a máquina estiver
/// ligada, ou desde o boot se instalado como serviço (<c>biblioteca servico-instalar</c>).
/// </summary>
public static class Lancador
{
    private enum Porta { DaBiblioteca, Livre, DeOutroPrograma }

    public static async Task<int> Abrir(Configuracao cfg)
    {
        switch (await Verificar(cfg.Porta))
        {
            case Porta.DeOutroPrograma:
                Console.Error.WriteLine($"A porta {cfg.Porta} está ocupada por outro programa. Escolha outra em appsettings.json (\"Biblioteca\": {{ \"Porta\": ... }}).");
                return 1;
            case Porta.Livre:
                Console.WriteLine($"Iniciando a Biblioteca Digital na porta {cfg.Porta}…");
                Iniciar();
                var prazo = DateTime.UtcNow.AddSeconds(90);
                while (await Verificar(cfg.Porta) != Porta.DaBiblioteca)
                {
                    if (DateTime.UtcNow > prazo)
                    {
                        Console.Error.WriteLine($"O servidor não respondeu em 90 s. Veja o registro em {cfg.Registro}");
                        return 1;
                    }
                    await Task.Delay(500);
                }
                break;
        }
        Console.WriteLine($"Biblioteca Digital no ar: {cfg.EnderecoLocal}");
        // BIBLIOTECA_SEM_NAVEGADOR: para verificação automática, sem abrir janela.
        if (Environment.GetEnvironmentVariable("BIBLIOTECA_SEM_NAVEGADOR") is not { Length: > 0 })
            Process.Start(new ProcessStartInfo(cfg.EnderecoLocal) { UseShellExecute = true });
        return 0;
    }

    /// <summary>
    /// Pergunta em 127.0.0.1, não em "localhost": no Windows, localhost tenta primeiro o IPv6, e
    /// uma recusa ali demora ~2 s — o lançador concluía "fora do ar" com o servidor de pé.
    /// </summary>
    private static async Task<Porta> Verificar(int porta)
    {
        try
        {
            using var tcp = new TcpClient();
            using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync("127.0.0.1", porta, prazo.Token);
        }
        catch { return Porta.Livre; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var corpo = await http.GetStringAsync($"http://127.0.0.1:{porta}/estado");
            return corpo.Contains("\"biblioteca\"", StringComparison.OrdinalIgnoreCase) ? Porta.DaBiblioteca : Porta.DeOutroPrograma;
        }
        catch (HttpRequestException) { return Porta.DeOutroPrograma; }
        catch (TaskCanceledException) { return Porta.Livre; } // aceitou conexão mas ainda não responde: subindo
    }

    /// <summary>Sobe o próprio executável com <c>servir</c>, sem janela, desligado deste processo.</summary>
    private static void Iniciar()
    {
        var exe = Environment.ProcessPath!;
        // No Windows, pelo shell e com janela oculta: o servidor não herda a janela nem os canais
        // de quem o abriu e segue no ar depois que o lançador termina.
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = OperatingSystem.IsWindows(),
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        // Rodando como "dotnet biblioteca.dll" (Linux): o dotnet precisa do caminho da dll.
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "biblioteca.dll"));
        psi.ArgumentList.Add("servir");
        Process.Start(psi);
    }
}
