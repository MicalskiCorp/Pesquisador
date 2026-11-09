# Operação

Para quem instala e mantém a Biblioteca Digital na máquina da instituição.

## O que existe em disco

| Pasta | O que é | Onde mora |
|---|---|---|
| `acervo/` | PDFs originais, fichas (`.ficha.json`), textos (`.md`), `colecoes.json`, `lexico.txt`, `DESFALQUES.md` | fora do git do programa; a instituição guarda e faz backup como preferir (ver Backup do acervo) |
| `dados/busca.db` | índice de busca (~300 MB) | fora do git: refaz-se com `reindexar` |
| `dados/envios/` | PDFs enviados pela tela e ainda não confirmados | fora do git; apagados em 24 h |
| `dados/biblioteca.log` | registro do servidor (erros, inclusões) | fora do git |
| `publicado/BibliotecaDigital/` | o programa: baixado da Release (assinado) pelo `configurar`, ou gerado pelo `publicar.ps1` | fora do git |

## Instalar

Na máquina da instituição (Windows), com o [Git](https://git-scm.com/) instalado:

```powershell
git clone https://github.com/MicalskiCorp/Pesquisador C:\BibliotecaDigital
```

Se a instituição já tem um acervo, copie-o para `C:\BibliotecaDigital\acervo` antes do próximo passo.

Depois, duplo-clique em `C:\BibliotecaDigital\configurar.cmd`. Ele confere a pasta do acervo (e a
cria vazia, se não existir; **nunca baixa acervo de lugar nenhum**), baixa o Poppler, baixa o
`biblioteca.exe` **assinado** da última Release, cria o atalho "Biblioteca Digital" na área de
trabalho e monta o índice. Dentro dessa pasta, o programa encontra o acervo sozinho. O nome da
instituição, no topo da tela, vem de `acervo\biblioteca.json` (`{ "instituicao": "…" }`).

Por que assinado: com o Controle Inteligente de Aplicativos do Windows 11 ligado, executável
sem assinatura é bloqueado. Ver [assinatura.md](assinatura.md).

Para atualizar o programa, rode o `configurar.cmd` de novo: ele para a Biblioteca Digital que estiver no
ar e põe a última versão. Se estiver instalado como serviço, antes pare o serviço como
administrador (`sc.exe stop BibliotecaDigital`) e, depois, inicie de novo (`sc.exe start BibliotecaDigital`).

## Ficar no ar desde o boot e atender a rede

Num terminal **como administrador**:

```powershell
.\publicado\BibliotecaDigital\biblioteca.exe servico-instalar
```

Registra o serviço do Windows "Biblioteca Digital" (inicia com a máquina, reinicia se cair) e abre a
porta no firewall. Depois disso, qualquer computador da rede da instituição consulta em
`http://<nome-desta-máquina>:8080`. Para desfazer: `servico-remover`.

Sem o serviço, a Biblioteca Digital fica no ar enquanto o processo estiver rodando — o duplo-clique
sobe e ele continua até a máquina desligar.

**Porta**: 8080. Para mudar, edite `publicado\BibliotecaDigital\appsettings.json`
(`"Biblioteca": { "Porta": 8090 }`) e reinicie o serviço.

**Acervo fora do repositório**: em `appsettings.json`, `"Biblioteca": { "Acervo": "D:\\Acervo", "Dados": "D:\\BibliotecaDados" }`.

## Comandos

```
biblioteca.exe                    abre (inicia o servidor se preciso)
biblioteca.exe servir             só o servidor
biblioteca.exe ajuda              lista os comandos
```

| Comando | Faz |
|---|---|
| `reindexar` | refaz o índice de busca do zero (seguro a qualquer momento) |
| `auditar` | confere ficha × PDF × texto × fidelidade; termina com `SEM PROBLEMAS` ou a lista do que ver |
| `desfalques` | regrava `acervo/DESFALQUES.md` com as edições que faltam |
| `importar <pasta> --colecao id` | importa `<pasta>/<ano>/*.pdf` (remessas de centenas de PDFs; lotes menores se incluem pela tela) |
| `reextrair --todos` ou `reextrair <id>` | refaz o texto a partir do PDF e da ficha; a ficha não muda |
| `lexico` | remonta `acervo/lexico.txt` (depois de muitas inclusões); rode `reextrair --todos` em seguida |
| `artigos --de <pasta>` | importa um catálogo de artigos (formato do índice dos editores) |
| `buscar "termos" [--modo tolerante]` | busca pela linha de comando |
| `extrair <pdf>` | mostra o texto que sairia de um PDF, sem gravar nada |
| `servico-instalar` / `servico-remover` | serviço do Windows e regra de firewall (administrador) |

## Rotina

- **Inclusões pela tela**: nada a fazer — o documento entra no índice na hora.
- **Coleção cadastrada ou editada pela tela** (Curadoria → Coleções): muda `acervo/colecoes.json`,
  que entra no backup junto com o resto do acervo.
- **Backup do acervo** (fichas, erratas, documentos novos): ver abaixo. As fichas são o trabalho
  humano insubstituível.
- **Mensal**: Curadoria → Conferir o acervo (ou `auditar`).
- **Se a busca se comportar estranho**: Curadoria → Refazer o índice (ou `reindexar`).
- **A primeira busca depois de ligar** pode levar alguns segundos (o índice vem do disco); depois,
  menos de meio segundo.

## Restaurar numa máquina nova

```powershell
git clone https://github.com/MicalskiCorp/Pesquisador C:\BibliotecaDigital
# copie o acervo (do backup) para C:\BibliotecaDigital\acervo
C:\BibliotecaDigital\configurar.cmd                       # Poppler, programa, índice
.\publicado\BibliotecaDigital\biblioteca.exe auditar
```

## Backup do acervo

O acervo é a única coisa insubstituível: o programa se baixa de novo e o índice se refaz com
`reindexar`. Duas formas comuns:

- **Cópia** da pasta `acervo` inteira para um disco externo ou para um servidor de arquivos da
  instituição, numa rotina (diária ou semanal).
- **Repositório git próprio e privado** dentro de `acervo` (`git init`, PDFs pelo Git LFS), com
  `git add -A; git commit; git push` ao fim do dia. Guarda também o histórico de cada ficha. Os PDFs
  passam de 100 MB às vezes, então o LFS é necessário em serviços como o GitHub, e a cota gratuita
  costuma ser pequena para acervos grandes.

Nunca ponha o acervo no repositório do programa: ele é público.

## Desfalques

A relação de documentos que faltam em cada coleção numerada sai em `acervo/DESFALQUES.md`
(regerada com `desfalques`). Observações sobre cada desfalque ficam em `acervo/desfalques.json`,
editável.