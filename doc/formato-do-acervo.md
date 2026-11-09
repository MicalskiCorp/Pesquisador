# Formato do acervo

O molde permanente em que a Biblioteca Digital guarda cada documento. Vale para qualquer coleção
que venha a existir. Mudar este formato exige mudar a versão (`pesquisador/1`) e um
conversor; ninguém deve editá-lo sem isso.

## Três arquivos por documento, lado a lado

```
acervo/
  colecoes.json                                   coleções, campos, regra de pasta
  lexico.txt                                      palavras do acervo (decide hífen de fim de linha)
  DESFALQUES.md                                   edições que faltam (gerado: `biblioteca desfalques`)
  desfalques.json                                 observações sobre cada desfalque (editável)
  revista-exemplo/
    1980-1989/1988/
      Tomo XXIX - n.01 Janeiro.pdf                o ORIGINAL — nunca alterado
      Tomo XXIX - n.01 Janeiro.ficha.json         camada HUMANA — metadados, artigos, errata, histórico
      Tomo XXIX - n.01 Janeiro.md                 camada GERADA — texto por página
```

| Arquivo | Quem escreve | Pode ser refeito? |
|---|---|---|
| `.pdf` | inclusão ou substituição (pela tela ou `importar`) | não: é o original. Substituído, o anterior vai para `.substituidos/` |
| `.ficha.json` | pessoas (tela de curadoria) e a migração | **não**: é trabalho humano. Backup obrigatório |
| `.md` | o extrator, a partir do PDF e da ficha | **sim**: `biblioteca reextrair` refaz igual, byte a byte |
| `dados/busca.db` | o indexador | **sim**: `biblioteca reindexar` refaz do zero |

A pasta vem de `colecoes.json` (`"pastas": "{decada}/{ano}"`). O identificador do documento é
o caminho em slug: `revista-exemplo/1980-1989/1988/tomo-xxix-n-01-janeiro`.

## O `.md`

```markdown
---
formato: "pesquisador/1"
id: "revista-exemplo/1980-1989/1988/tomo-xxix-n-01-janeiro"
colecao: "revista-exemplo"
titulo: "Revista Exemplo — Tomo XXIX, n.º 01 (Janeiro 1988)"
ano: "1988"
tomo: "XXIX"
numero: "01"
meses: "Janeiro"
pdf: "Tomo XXIX - n.01 Janeiro.pdf"
pdf_sha256: "a1b2c3d4…"
pdf_paginas: "36"
ocr: "PDFCreator Version 1.1.0"
extrator: "1.0.0"
fidelidade: "36 conferidas, 0 divergentes, 0 sem texto"
---

# Revista Exemplo — Tomo XXIX, n.º 01 (Janeiro 1988)

<!-- p. 10 -->
## Página 10 · impressa 8
<!-- pagina pdf=10 impressa=8 impressa_origem=detectada colunas=2 fidelidade=conferida hifens=27 meia_largura=41 -->
<!-- margem: -8- -->
<!-- margem: Digitalizado pela instituição - Cidade - UF -->

:ltivando assim o progresso da região.

Em setembro de 1913 foi mpdido o lote do "Ribeirão Exemplo" e ocupado por famílias vindas de outros núcleos.

<!-- coluna 2 -->

caminhos vindos do interior cone·entram-se ali, e o povoado cresce· em volta da estação, …
```

### Regras de leitura (o que um programa pode assumir)

- **Cabeçalho**: entre `---`; cada linha `chave: valor`, valor em string JSON.
- **Página**: começa em `<!-- p. N -->` (N = página do PDF, a partir de 1). Ferramentas externas
  de citação podem achar a página por este marcador, sem ler o resto do formato.
- `## Página N · impressa X` é só para leitura humana; o dado está no comentário seguinte.
- `<!-- pagina … -->` — atributos da página:

  | Atributo | Significado |
  |---|---|
  | `pdf` | página do PDF |
  | `impressa` | número impresso; `36-37` em digitalização de página dupla |
  | `impressa_origem` | `detectada` (lida no rodapé/cabeçalho e confirmada pelo vizinho), `interpolada` (entre duas detectadas), `ajustada` (pela curadoria) |
  | `colunas` | colunas ou páginas impressas lado a lado (omitido quando 1) |
  | `fidelidade` | `conferida`, `divergente` ou `sem_texto` — ver abaixo |
  | `hifens` | hífens de fim de linha removidos (declarados para a conferência) |
  | `meia_largura` | caracteres decodificados da corrupção meia-largura |
  | `nao_lidos` | glifos que o PDF tem e a leitura não trouxe (indicador; a auditoria lista acima de 30) |

- `<!-- margem: … -->` — cabeçalho, rodapé, número de página e carimbo, **literais**, fora do corpo.
- `<!-- coluna N -->` — o corpo muda de coluna (ou de página impressa, em página dupla). Um
  parágrafo que começa em minúscula logo depois é continuação da frase da coluna anterior.
- **Corpo**: um parágrafo por linha, separados por linha em branco.
- **Escape**: o texto de OCR passa por escape de Markdown — `\` antes de `` \ ` * _ [ ] < > ~ & | ``
  em qualquer lugar e de `# + - =` no início, e na pontuação de `1.`/`1)` no início. Ler é tirar a
  barra. Assim um `#` ou `~~` vindo do OCR nunca vira título ou tachado na tela.
- Comentário que não seja um dos acima é anotação e não faz parte do texto.

## O que o extrator faz com o texto — e só isto

O texto vem da **camada de OCR do PDF**, sem refazer o OCR. Erro de reconhecimento fica: é o
que o PDF diz (`m pdido`, `edificaqáo`), e é o que o Ctrl+F no PDF encontra. A correção humana
vai para a errata da ficha.

| Transformação | Por quê |
|---|---|
| Ordem de leitura própria (corte XY sobre as palavras) | colunas e páginas duplas saíam intercaladas linha a linha |
| Palavras que o OCR espaçou voltam a se juntar (vão < 0,12 da altura da letra) | `T i vemos` → `Tivemos` |
| Linhas do mesmo parágrafo se juntam | busca por frase atravessa a quebra de linha |
| Hífen de fim de linha sai, decidido pelo léxico do acervo | `par-|te` → `parte`; `tornou-|se` fica |
| Espaço antes de pontuação sai, espaços repetidos viram um | só espaço |
| Meia-largura (U+FF20–U+FFFF) volta ao Latin-1/Windows-1252 | defeito de codificação encontrado em PDFs reais |
| Cabeçalho, rodapé, número de página e carimbo vão para `margem` | não aparecem no meio do texto, mas continuam no arquivo |

## Fidelidade — a garantia de que o texto bate com o PDF

Para cada página, o extrator compara o **multiconjunto de caracteres** (espaço não conta) do que
gravou — margens + corpo + hífens declarados — com as palavras da camada de texto do PDF.
`conferida` quer dizer: **nenhum caractere perdido, nenhum inventado, nenhum trocado**. Só ordem,
quebra de linha e espaço mudaram.

O total de páginas conferidas, divergentes e sem texto do acervo inteiro sai em `biblioteca auditar`.

O indicador `nao_lidos` é a segunda opinião, de uma biblioteca independente (PdfPig): posições de
glifo que o PDF tem menos caracteres lidos. Há OCRs que empilham glifos (dezenas de cópias no
mesmo ponto) e deixa lixo na borda do scanner, que a leitura descarta com razão — por isso é
indicador e não selo. A auditoria lista as páginas acima de 30 para um olhar humano.

## A ficha (`.ficha.json`)

```json
{
  "formato": "pesquisador/ficha/1",
  "id": "revista-exemplo/1950-1959/1957/tomo-i-n-01-novembro",
  "colecao": "revista-exemplo",
  "titulo": "Revista Exemplo — Tomo I, n.º 01 (Novembro 1957)",
  "campos": { "ano": "1957", "tomo": "I", "numero": "01", "meses": "Novembro" },
  "pdf": {
    "arquivo": "Tomo I - n.01 Novembro.pdf", "bytes": 4500000, "sha256": "9f2c…", "paginas": 24,
    "produtor": "Adobe Acrobat 9.0 Paper Capture Plug-in",
    "origem": "E:\\Remessa\\1957\\Tomo I - n.01 Novembro.pdf",
    "urlPublica": "https://acervo.exemplo.org/1957/tomo-i-n-01.pdf"
  },
  "paginasImpressas": { "2": "ii" },
  "artigos": [ { "titulo": "…", "autor": "…", "secao": "…", "paginas": "2-3", "fonte": "…" } ],
  "errata": [ { "id": "…", "pagina": 10, "literal": "m pdido", "leitura": "medido", "por": "…", "em": "…" } ],
  "historico": [ { "em": "…", "por": "migração", "acao": "incluído", "detalhe": "…" } ]
}
```

- `paginas` dos artigos são **impressas**, como o catálogo dos editores registra; a busca converte
  para a página do PDF pelo mapa de páginas impressas do `.md`.
- A errata só é aceita se o `literal` existir exatamente naquela página. A busca indexa as duas
  formas; a tela mostra a leitura corrigida sublinhada e o literal ao passar o mouse.

## Coleções — os tipos de acervo

Cada coleção é um tipo de acervo (uma revista, um jornal, um fundo documental…),
definido em `acervo/colecoes.json` e cadastrado pela tela (Curadoria → Coleções). Todo documento
de toda coleção passa pelo mesmo caminho e tem o mesmo comportamento: PDF com OCR → texto por
página conferido → ficha → índice.

```json
{
  "id": "jornal-nsc", "nome": "Jornal NSC",
  "pastas": "{ano}/{mes}",
  "titulo": "NSC — {data} — {caderno}",
  "campos": [
    { "chave": "data", "rotulo": "Data", "tipo": "data", "obrigatorio": true },
    { "chave": "edicao", "rotulo": "Edição", "tipo": "numero" },
    { "chave": "caderno", "rotulo": "Caderno", "tipo": "opcoes", "opcoes": ["Geral", "Esportes"] }
  ],
  "nomesDeArquivo": [ "^(?<data>\d{2}-\d{2}-\d{4})" ],
  "extracao": { "desfazerMeiaLargura": false, "margens": [] },
  "numeracao": { "grupo": "volume", "numero": "edicao", "faixas": [ { "edicoes": 365 } ] }
}
```

| Chave | O que faz |
|---|---|
| `id` | identificador; entra no caminho dos arquivos e **não muda** depois que a coleção tem documentos |
| `pastas` | subpastas, com campos entre chaves. `{ano}`, `{decada}` e `{mes}` saem do campo de tipo ano ou data. Vazio: tudo na pasta da coleção |
| `titulo` | modelo do título de exibição |
| `campos[].tipo` | `texto`, `numero`, `ano`, `data` (dd/mm/aaaa, mm/aaaa ou aaaa) ou `opcoes` (com a lista em `opcoes`) |
| `nomesDeArquivo` | opcional: expressões com grupos nomeados que sugerem campos a partir do nome do arquivo |
| `extracao.margens` | opcional: linhas fixas (carimbo, cabeçalho) que saem do corpo e vão para a margem |
| `numeracao` | opcional, para periódicos: em cada grupo (`grupo`, ex. tomo), os números de 1 a `edicoes` (por faixa de anos). É o que calcula os desfalques. Sem numeração, a coleção não tem desfalques |

Os campos que formam o caminho da pasta ficam travados na ficha depois da inclusão: o documento
não muda de lugar por edição de formulário.
