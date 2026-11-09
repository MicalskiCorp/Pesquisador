# Manual do curador

Para quem usa a Biblioteca Digital no dia a dia da instituição.

## Buscar

Digite e tecle Enter. A busca é em **todo o acervo**; em **Onde**, escolha uma coleção para
procurar só nela (entre as que forem cadastradas). Cada resultado é um
**documento**, com a coleção dele; dentro, as **páginas do PDF** em que o termo aparece, o número
**impresso** da página, o artigo (quando catalogado) e o trecho com o termo destacado.

| Para | Digite |
|---|---|
| todas as palavras numa mesma página | `ponte ferro` |
| uma frase exata | `"ponte de ferro"` |
| palavras que começam assim | `coloni*` (colônia, colonização, colonizadores…) |
| excluir um termo | `enchente -1983` |

**Como** (o modo da busca):

- **Exato** — acento e maiúscula não importam; o resto, sim.
- **Variações** — também plural, feminino e diminutivo (colono → colonos, colona).
- **Tolerante a OCR** — também as grafias que o OCR errou e que existem no acervo
  (edificação → edificaqáo). A lista de grafias incluídas aparece no topo; desmarque o que não servir.

Filtros: **período** (de/até) e **autor** do artigo.

**Não achou?** O texto vem da leitura automática (OCR) feita na digitalização, que erra.
Tente o modo tolerante, a grafia da época (`êste`, `côr`, `pharmacia`, `Destêrro`) ou só o
começo da palavra com `*`.

## Ler um documento

À esquerda, as **páginas do PDF original**; à direita, o **texto do acervo** na ordem de leitura
(colunas separadas), com o termo procurado marcado nos dois lados.

- **Rolagem vinculada** (ligada por padrão): rolar o PDF rola o texto até a mesma página, no mesmo
  ponto — e vice-versa. Desmarque para rolar cada lado sozinho.
- **Clicar num parágrafo** do texto marca o parágrafo e leva o PDF à página e ao ponto dele.
- **Ir a uma página**: digite o número em "Página", clique em "Página N" no texto ou numa das
  páginas da faixa "nesta edição".
- **− e +** aumentam ou diminuem o PDF; **PDF original** abre o arquivo inteiro no navegador.
- O selo verde **conferida** quer dizer que cada caractere daquela página está igual ao PDF.

O texto é o que o OCR leu. Palavra estranha (`m pdido`) quase sempre é erro de leitura: confira
na imagem à esquerda.

## Incluir documentos

**Curadoria → Incluir no acervo.** Um PDF ou vários de uma vez.

1. Escolha a **coleção** (uma das cadastradas) e os PDFs — com o texto do
   OCR, como saem do scanner com OCR ligado. Clique em **Enviar e conferir**.
2. Os arquivos são lidos um por vez; a página mostra "lendo X de N" e se atualiza sozinha.
3. Aparece a **tabela do lote**: uma linha por arquivo, com os campos da coleção, quantas páginas
   foram conferidas e **onde o arquivo vai ser gravado**. O sistema sugere campos pelo nome do
   arquivo e o ano pela capa — confira. Um padrão de nome fixo por coleção (num periódico por
   tomos, por exemplo, `Tomo LXVI - n.01 Janeiro-fevereiro.pdf`) deixa os campos já preenchidos.
   - **Preencher em todos** copia um valor (o ano, o caderno) para todas as linhas.
   - **Prévia** abre o PDF e o texto extraído lado a lado. "Este PDF não tem camada de texto"
     quer dizer que ele é só imagem: precisa passar pelo OCR antes.
   - Arquivo **já no acervo** (o mesmo PDF, byte a byte) vem desmarcado, com o link para o que já existe.
4. Clique em **Incluir os marcados**. O que estiver certo entra; o que faltar volta com o motivo,
   linha por linha, e fica no lote para corrigir. **Descartar o lote** apaga o que sobrou.

O que entra fica na busca na hora.

## Cadastrar uma coleção

**Curadoria → Coleções.** Uma coleção é um tipo de acervo — uma revista, um jornal, um
fundo de documentos. Todas seguem o mesmo caminho (PDF com OCR → texto conferido página por página
→ ficha → busca); o que muda é a ficha, as pastas e o título. Qualquer pessoa da curadoria pode
cadastrar.

1. Em **Nova coleção**, comece de um modelo — **Periódico** (jornal, revista, boletim), **Livro**,
   **Documento avulso** ou **Em branco**. Tudo se ajusta antes de gravar.
2. Dê o **nome** (o identificador sai dele e vira o nome da pasta: `Jornal NSC` → `jornal-nsc`).
3. **Campos da ficha**: rótulo, tipo e se é obrigatório. Os tipos são texto, número, ano, data
   (dd/mm/aaaa, mm/aaaa ou só o ano) e lista (escreva as opções separadas por vírgula). A chave
   sai do rótulo se ficar em branco.
4. **Organização das pastas**: tudo junto, por ano, por década e ano, ou por ano e mês. Ano e mês
   saem do campo de ano ou de data.
5. **Modelo do título**, com os campos entre chaves: `{colecao} — {data} — {caderno}`.
6. **Numeração e desfalques**, para periódicos: o campo do grupo (volume, tomo), o do número e
   quantas edições tem cada grupo. Com isso a coleção entra em **Desfalques**.
7. Em **Avançado**: expressões que sugerem campos pelo nome do arquivo e linhas de carimbo
   ("Digitalizado por…") que saem do corpo do texto.

O sistema recusa o que quebraria o acervo — identificador repetido, pasta ou título citando campo
que não existe, expressão inválida — e diz o quê. A coleção fica em `acervo/colecoes.json`, junto
do acervo, e vai para o git com ele. Depois que uma coleção tem documentos, o identificador não muda;
pastas novas valem para os próximos documentos.

## Corrigir

No documento, **Editar ficha**:

- **Campos e título.** Os campos que decidem a pasta (o ano; num jornal por mês, a data) não se
  editam aqui; para mudar, inclua de novo.
- **Páginas impressas.** A numeração é detectada no rodapé; onde errar, informe "página do PDF →
  impressa". O ajuste vence a detecção.
- **Artigos.** Título, autor e páginas impressas, como no sumário da revista.
- **Substituir o PDF** por uma digitalização melhor. O anterior não se perde (fica em
  `.substituidos/`).

**Registrar errata** — quando a imagem mostra uma palavra e o OCR leu outra:

1. Selecione o trecho errado no texto (painel da direita) — aparece o botão **Registrar errata deste trecho**.
2. Confira a página e o trecho, escreva a leitura correta.

O texto original continua guardado como o PDF diz; a correção passa a ser achada pela busca e
aparece sublinhada (passe o mouse para ver o literal). Toda alteração fica no **histórico** da
ficha com o seu nome — preencha "Seu nome" uma vez, o navegador lembra.

Ao fim do dia (ou da semana), quem cuida do servidor guarda o trabalho no git — ver
[operação](operacao.md#rotina).

## Conferir o acervo

**Curadoria → Conferir o acervo** mostra quantas páginas estão conferidas e o que precisa de
atenção. "Refazer o índice de busca" é seguro a qualquer momento.

**Curadoria → Desfalques** lista, para cada coleção com numeração, as edições que deveriam existir
e não estão no acervo. Quando uma delas for digitalizada, inclua pela tela: ela sai da lista sozinha.
