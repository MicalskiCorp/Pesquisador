# Biblioteca Digital — instruções para o Claude Code

Biblioteca digital genérica para acervos de documentos digitalizados (PDF com OCR). Visão geral em
[README.md](README.md).

## Regra dura: este repositório é público e não é de nenhum acervo

O código serve a qualquer instituição. **Nada** de um acervo específico entra aqui — nome de
instituição, cidade ou coleção real, trecho de documento, nome de arquivo real, caminho de máquina,
inventário, plano de trabalho de um cliente. Exemplos usam a coleção fictícia "Revista Exemplo"
(`tests/BibliotecaDigital.Testes/revista-exemplo.json`). Casos de teste que precisam do acervo
real (bancada do extrator, gabarito de busca) ficam no próprio acervo, em `acervo/testes/*.json`.
O nome da instituição na tela vem de `acervo/biblioteca.json`. O `configurar` **nunca** baixa acervo.

## Regra dura: nenhuma IA no produto

A IA constrói o projeto; o produto roda sem ela — na consulta **e na curadoria** (inclusão,
cadastro de coleção, ficha, errata). **Nunca** adicionar SDK de LLM, embeddings, busca vetorial,
ML.NET, ONNX, motor de OCR (Tesseract e afins são redes neurais) ou serviço externo de qualquer
tipo — nem "opcional", nem "só para sugerir". Busca é FTS5 + radical por regra + distância de
edição; extração é Poppler + geometria; sugestões de campo são expressões regulares. O sistema
funciona sem internet.

`ArquiteturaTestes` reprova o build se isso mudar: pacote proibido (direto ou indireto), cliente
de rede fora do `Lancador` (que só fala com 127.0.0.1), endereço que não seja desta máquina,
programa externo além de Poppler (`pdftotext`, `pdfinfo`, `pdftoppm`), `sc.exe`/`netsh` e o
navegador, e página que carregue algo de fora. **Não afrouxe esses testes** para fazer uma
mudança passar: a mudança é que está errada.

## Regra dura: o texto do PDF não se corrige

O `.md` guarda o que a camada de OCR do PDF diz, com erro e tudo. O extrator só pode fazer as
transformações listadas em [doc/formato-do-acervo.md](doc/formato-do-acervo.md) — ordem de
leitura, junção de linha, hífen de fim de linha, espaço, meia-largura, margem. A conferência de
fidelidade (`ConferenciaDeFidelidade`) prova isso página a página; **toda mudança no montador
tem de manter 100% de páginas conferidas** (bancada e `biblioteca auditar`). Correção humana vai para a errata da ficha.

## Onde está o quê

| Projeto | Responsabilidade |
|---|---|
| `BibliotecaDigital.Acervo` | modelo e disco: `Colecao`, `Ficha`, `FormatoMarkdown`, `RepositorioDoAcervo` |
| `BibliotecaDigital.Extracao` | PDF → página: `LeitorPoppler`, `OrdemDeLeitura` (corte XY), `MontadorDePagina`, `ConferenciaDeFidelidade`, `PaginacaoImpressa`, `Lexico`, `Importador`, `Auditoria` |
| `BibliotecaDigital.Busca` | `BancoDeBusca` (esquema), `Indexador`, `MotorDeBusca`, `Consulta`, `RadicalPortugues`, `DistanciaOcr` |
| `BibliotecaDigital.App` | o executável `biblioteca`: `Lancador` (abre ou inicia), `Servidor`, `Cli`, `Operacoes`, `Desfalques`; telas em `Pages/` (Razor, renderizadas no servidor) e `wwwroot/` (CSS e um JS pequeno, sem biblioteca) |

Dados: `acervo/` (verdade, da instituição, ignorado pelo git do código) e `dados/busca.db` (derivado, fora do git). Nunca gravar no acervo
por fora de `Importador` e da API de curadoria — eles mantêm ficha, PDF e texto coerentes.

## Convenções

- Tudo em português: projetos, classes, métodos, variáveis, comentários, textos de tela.
- Comentário explica **por quê** — e, quando a regra veio de um caso real do acervo, cita o caso
  (`1988, p. 10`), para a próxima pessoa poder conferir no PDF.
- Mudou regra do montador? Suba `Extrator.Versao`, rode a bancada (`dotnet test`) e
  `biblioteca reextrair --todos` + `auditar`.
- Tela sóbria e autossuficiente: C# no servidor (Razor Pages), CSS próprio, JS mínimo, **nada
  carregado de fora** (`A_tela_nao_carrega_nada_de_fora` reprova CDN, fonte remota, script externo).
  Sem Node, sem Docker. Cores em `wwwroot/estilo.css`.
- O PDF abre no visualizador do navegador (`/pdf?id=…#page=N`), servido inline com intervalo de bytes.

## Comandos

```sh
dotnet build src/BibliotecaDigital.slnx && dotnet test src/BibliotecaDigital.slnx
dotnet run --project src/BibliotecaDigital.App -- servir      # tela em http://localhost:8080
dotnet run --project src/BibliotecaDigital.App -- auditar
.\publicar.ps1 -Poppler "<bin do Poppler>"               # o pacote Windows autossuficiente
.\configurar.ps1 [-Compilar]                             # máquina do zero: Poppler, programa, atalho, índice
```

No Windows, o `pdftotext` do Git Bash é o do xpdf (sem `-bbox-layout`); o Poppler de verdade
precisa estar em `BIBLIOTECA_POPPLER`. O `LeitorPoppler` recusa o xpdf com mensagem clara.

## Git

`MicalskiCorp/Pesquisador` — **público**, licença MIT: só o programa. Uma tag `v*` dispara
`.github/workflows/pacote.yml`, que testa, publica e assina o `biblioteca.exe` pela SignPath
Foundation (gratuito para código aberto; o Controle Inteligente de Aplicativos do Windows 11
bloqueia exe sem assinatura). Ver `doc/assinatura.md`.

O repositório ainda tem o nome antigo do projeto (Pesquisador) porque o pedido à SignPath foi
feito com ele; renomear só depois da aprovação, e então atualizar os endereços no README, no
`configurar.ps1` e na documentação. Os identificadores de formato `pesquisador/1` e
`pesquisador/ficha/1` **não** mudam com o nome: estão gravados em cada arquivo de acervo.

`main` recebe o que está pronto; desenvolvimento em branch. Não dar push sem pedido explícito.