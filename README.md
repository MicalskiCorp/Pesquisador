# Biblioteca Digital

Biblioteca digital para acervos de documentos digitalizados: revistas, jornais, livros, fundos
documentais. Lê os PDFs que já têm OCR, guarda o texto de cada página num formato estruturado e
ligado ao PDF original, e responde **em qual documento, em qual página do PDF e em qual página
impressa** um nome, lugar, data ou assunto aparece.

- **Um programa só.** `biblioteca.exe` traz o .NET, o servidor, a tela e o Poppler. Não precisa
  instalar nada, nem Docker, nem banco de dados: o índice é um arquivo.
- **Abre ou inicia.** Duplo-clique: se a Biblioteca Digital já está no ar, abre o navegador; se não,
  sobe o servidor sem janela e então abre. Pela rede, qualquer máquina da instituição acessa
  `http://<máquina>:8080`. Como serviço do Windows, fica no ar desde o boot.
- **Sem IA dentro — na consulta e na curadoria.** Índice invertido (SQLite FTS5), regra fixa de
  radical do português e distância de edição para erro de OCR; o texto sai da camada de OCR que
  já vem no PDF, montado por geometria; sugestões de campo são expressões regulares. Funciona sem
  internet e não chama serviço nenhum. Os testes de arquitetura reprovam o build se entrar
  pacote de IA (mesmo indireto), chamada de rede para fora ou programa externo desconhecido.
- **O texto bate com o PDF.** Cada página é conferida caractere por caractere contra a camada
  de texto do PDF: nada perdido, nada inventado, nenhuma letra trocada.
- **Qualquer tipo de acervo.** Cada coleção (uma revista, um jornal, um fundo de documentos) define
  a ficha, as pastas e o título; o caminho é o mesmo para todas. A curadoria cadastra coleções e
  inclui PDFs em lote pela tela.
- **PDF e texto lado a lado.** As páginas do PDF e o texto rolam juntos; clicar num parágrafo mostra
  a página dele no PDF.
- **O acervo é seu e fica à parte.** Este repositório é só o programa (licença MIT). O acervo — PDFs,
  fichas, textos — mora na pasta `acervo\`, fora do git do programa, e nunca é enviado a lugar
  nenhum. Cada instituição guarda e faz backup do seu como preferir.

## Usar

Precisa do Windows 10 ou 11 e do [Git](https://git-scm.com).

**1. Trazer o programa:**

```powershell
git clone https://github.com/MicalskiCorp/Pesquisador C:\BibliotecaDigital
```

**2. Configurar.** Na pasta `C:\BibliotecaDigital`, dê **duplo-clique em `configurar.cmd`**. Ele roda
como usuário comum (não precisa de administrador):

| Passo | O que faz | Se já estiver pronto |
|---|---|---|
| Acervo | confere a pasta `acervo\`; se não existir, cria uma vazia | pula |
| Poppler | baixa a versão mais recente para `C:\poppler` | pula |
| Programa | baixa da última Release o `biblioteca.exe` **assinado** e põe o Poppler junto, em `publicado\BibliotecaDigital\` | atualiza |
| Atalho | cria **Biblioteca Digital** na Área de Trabalho | refaz |
| Índice | `biblioteca.exe reindexar`, que monta `dados\busca.db` | pula |

Pode rodar de novo quando quiser: o que já está pronto é pulado, e o programa vai para a última
versão (se a Biblioteca Digital estiver aberta, ele a para antes). Só a configuração usa a
internet; a Biblioteca Digital em si funciona sem ela.

**3. Montar o acervo.** Duas formas:

- **Começar do zero:** abra a Biblioteca Digital, cadastre uma coleção em *Curadoria → Coleções*
  (há modelos de periódico, livro e documento avulso) e inclua os PDFs em *Curadoria → Incluir*.
- **Trazer um acervo que já existe:** copie-o (ou clone o repositório onde a instituição o guarda)
  para `C:\BibliotecaDigital\acervo\` antes de rodar o `configurar.cmd`. Em outro lugar, aponte
  a variável `BIBLIOTECA_ACERVO`.

**4. Abrir** pelo atalho **Biblioteca Digital** da Área de Trabalho. A tela abre em `http://localhost:8080`.
O nome da instituição, no topo da tela, vem de `acervo\biblioteca.json`
(`{ "instituicao": "Nome da instituição" }`).

Para ficar no ar desde o boot e atender a rede: `biblioteca.exe servico-instalar` (como
administrador). Detalhes em [doc/operacao.md](doc/operacao.md).

<details>
<summary>Se aparecer "Uma política de Controle de Aplicativo bloqueou este arquivo"</summary>

É o **Controle Inteligente de Aplicativos** do Windows 11, que bloqueia programa sem assinatura.
O `biblioteca.exe` da Release é assinado e passa. O bloqueio só acontece com o exe **compilado
na máquina**: com `-Compilar`, ou quando ainda não há Release publicada. Nesse caso, use a
Release ou desligue o recurso em *Segurança do Windows > Controle de aplicativos e do navegador*.
Mais em [doc/assinatura.md](doc/assinatura.md).
</details>

<details>
<summary>Passo a passo à mão, sem o <code>configurar</code></summary>

```powershell
# o Windows recusa .ps1 por padrão; libere para o seu usuário (uma vez)
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned

# Poppler: https://github.com/oschwartz10612/poppler-windows/releases (Release-*.zip), extraia em C:\poppler
# Programa: baixe BibliotecaDigital-win-x64.zip da última Release e extraia em publicado\BibliotecaDigital;
#           copie o conteúdo da pasta bin do Poppler para publicado\BibliotecaDigital\poppler
.\publicado\BibliotecaDigital\biblioteca.exe reindexar
.\publicado\BibliotecaDigital\biblioteca.exe
```

Para compilar em vez de baixar: `winget install --id Microsoft.DotNet.SDK.10 -e`, e depois
`.\publicar.ps1 -Poppler "C:\poppler\poppler-26.09.0\Library\bin" -Atalho` (com a pasta **real**
do `pdftotext.exe`; o do Git é o xpdf e não serve).
</details>

## Política de assinatura de código

Assinatura de código gratuita fornecida por [SignPath.io](https://about.signpath.io), com
certificado da [SignPath Foundation](https://signpath.org).

- Autores, revisores e aprovadores: [MicalskiCorp](https://github.com/MicalskiCorp), dono do repositório

Só é assinado o que o [fluxo do GitHub Actions](.github/workflows/pacote.yml) compila a partir
deste repositório.

**Privacidade:** este programa não transfere informação nenhuma para outros sistemas em rede. Ele
atende só a quem acessa o servidor dele, na própria máquina ou na rede local.

## Documentação

| | |
|---|---|
| [doc/manual-do-curador.md](doc/manual-do-curador.md) | buscar, ler, incluir e corrigir |
| [doc/operacao.md](doc/operacao.md) | instalar, iniciar com o Windows, rede, backup, comandos |
| [doc/formato-do-acervo.md](doc/formato-do-acervo.md) | o molde: como cada página é guardada |
| [doc/assinatura.md](doc/assinatura.md) | a assinatura do executável e como sai uma versão |

## Desenvolvimento

Requer .NET 10 SDK e o Poppler (pasta `bin` em `BIBLIOTECA_POPPLER`). `.\configurar.ps1 -Compilar`
deixa os dois prontos e compila na máquina, sem baixar a Release.

```sh
dotnet build src/BibliotecaDigital.slnx
dotnet test src/BibliotecaDigital.slnx                          # bancada e gabarito: casos em acervo/testes/
dotnet run --project src/BibliotecaDigital.App -- servir        # http://localhost:8080
```

A bancada do extrator e o gabarito de busca conferem PDFs e consultas reais. Como dependem do
acervo, os casos ficam nele (`acervo/testes/bancada.json` e `gabarito.json`); sem eles, os testes
registram que não rodaram.

```
src/BibliotecaDigital.Acervo     o modelo: coleção, ficha, formato do .md, repositório em disco
src/BibliotecaDigital.Extracao   PDF → texto: Poppler, ordem de leitura, montagem, fidelidade, paginação
src/BibliotecaDigital.Busca      índice SQLite FTS5, consulta, radical, tolerância a OCR
src/BibliotecaDigital.App        o executável: lançador, servidor, páginas (Razor), linha de comando
  Pages/                         as telas, renderizadas no servidor em C#
  wwwroot/                       estilo.css e um pequeno biblioteca.js, sem biblioteca externa
tests/                           regras, arquitetura, bancada do extrator, gabarito de busca
configurar.cmd / .ps1            prepara a máquina: Poppler, programa, atalho, índice
.github/workflows/               gera, testa e assina o pacote (SignPath) a cada tag v*
publicar.ps1                     gera o pacote publicado\BibliotecaDigital
config/colecoes.json             modelo das coleções de um acervo novo (vazio: cadastre pela tela)
acervo/                          o acervo da instituição (fora do git do programa)
```
