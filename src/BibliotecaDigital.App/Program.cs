using BibliotecaDigital.App;

// Um executável, três usos:
//   biblioteca              abre o navegador; se o servidor não estiver no ar, sobe antes
//   biblioteca servir       o servidor (é o que o serviço do Windows roda)
//   biblioteca <comando>    operação pela linha de comando (importar, reindexar, auditar…)
Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length > 0 && Cli.EhComando(args[0]))
    return await Cli.Executar(args);

var cfg = Configuracao.Carregar();
if (args.Length == 0 || args[0] == "abrir")
    return await Lancador.Abrir(cfg);
if (args[0] == "servir")
    return Servidor.Executar(cfg, args[1..]);

Console.Error.WriteLine($"Comando desconhecido: {args[0]}. Veja: biblioteca ajuda");
return 1;
