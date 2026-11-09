# Assinatura de código

O Windows 11 com o **Controle Inteligente de Aplicativos** ligado bloqueia programa sem assinatura
de autoridade reconhecida ("Uma política de Controle de Aplicativo bloqueou este arquivo"). Um
certificado gerado por conta própria não serve. A Biblioteca Digital é assinada de graça pela
[SignPath Foundation](https://signpath.org), que atende projetos de código aberto. Isso só funciona
porque o código é público (MIT) e compilado no GitHub Actions, onde qualquer pessoa pode
conferir de qual código saiu o executável.

O Poppler (`pdftotext`, `pdfinfo`, `pdftoppm`) não é assinado, mas o Windows o deixa rodar pela
reputação, porque é muito distribuído. Isso foi conferido com o recurso ligado em 09/11/2026.

## O programa e o acervo

Este repositório é público e tem só o programa: código, documentação e o fluxo que gera e assina
o pacote. O acervo de cada instituição fica fora dele, na pasta `acervo/` (ignorada pelo git), e
nunca é enviado para lugar nenhum. O programa procura o acervo ali por padrão
(`Configuracao.AcharRaizDoProjeto`). Em outro lugar, use `BIBLIOTECA_ACERVO` ou a chave
`Biblioteca:Acervo` do `appsettings.json`.

## Como sai uma versão

```powershell
git tag v1.0.0
git push origin v1.0.0
```

O fluxo [.github/workflows/pacote.yml](../.github/workflows/pacote.yml) roda os testes, publica o
`biblioteca.exe` (win-x64, autossuficiente), manda para a SignPath, confere a assinatura e cria a
Release com `BibliotecaDigital-win-x64.zip`. O `configurar.ps1` baixa esse zip. Na SignPath Foundation,
cada assinatura de versão precisa de **aprovação manual** de quem tem o papel de aprovador: o fluxo
fica esperando até alguém aprovar no painel da SignPath.

## Cadastro na SignPath (uma vez)

1. Deixar `MicalskiCorp/Pesquisador` público, com `LICENSE` e a seção "Política de assinatura de
   código" do README.
2. Pedir a adesão em <https://signpath.org/apply>, informando o repositório.
3. Aprovado o pedido, no painel da SignPath:
   - instalar o **SignPath GitHub App** no repositório;
   - criar o projeto com slug `biblioteca` ligado ao repositório;
   - criar a política de assinatura `release-signing`;
   - em *Artifact configuration*, colar:
     ```xml
     <?xml version="1.0" encoding="utf-8"?>
     <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
       <zip-file>
         <pe-file path="biblioteca.exe">
           <authenticode-sign/>
         </pe-file>
       </zip-file>
     </artifact-configuration>
     ```
   - criar um usuário de CI e gerar o token de API.
4. No GitHub, em *Settings > Secrets and variables > Actions*: o secret `SIGNPATH_API_TOKEN` e a
   variável `SIGNPATH_ORGANIZATION_ID`.

O nome do slug ou da política pode mudar no cadastro. Nesse caso, ajuste `project-slug` e
`signing-policy-slug` no `pacote.yml`.

## Enquanto a assinatura não sai

Sem Release, o `configurar` compila na própria máquina. Isso funciona onde o Controle Inteligente
de Aplicativos está desligado (o `configurar` avisa quando está ligado).
