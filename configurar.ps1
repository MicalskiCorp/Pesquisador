# Configura uma máquina Windows e deixa a Biblioteca Digital pronta para abrir:
#   1. o acervo: confere a pasta acervo\ (cria vazia, se não existir; nunca baixa acervo);
#   2. o Poppler: baixa para C:\poppler, se faltar;
#   3. o programa: baixa o pacote assinado da última Release (padrão) ou compila (-Compilar);
#   4. o atalho na Área de Trabalho;
#   5. o índice de busca, se ainda não existir.
#
# Rodar na raiz do repositório do código, como usuário comum (não precisa de administrador):
#   configurar.cmd                          duplo-clique; contorna a política de scripts
#   .\configurar.ps1 [-Compilar] [-Poppler <bin>] [-SemAtalho] [-SemIndice]
#
# Pode rodar de novo: o que já está pronto é pulado, e o programa é atualizado para a última
# Release. Só esta configuração usa a internet (GitHub, winget); a Biblioteca Digital funciona sem ela.
param(
    [switch]$Compilar,                                      # gera o exe aqui (desenvolvimento)
    [string]$Poppler,                                       # pasta bin de um Poppler já instalado
    [string]$PastaPoppler = "C:\poppler",                   # onde baixar o Poppler, se preciso
    [string]$RepositorioCodigo = "MicalskiCorp/Pesquisador",     # de onde vem o programa (Releases)
    [switch]$SemAtalho,
    [switch]$SemIndice
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # a barra de progresso do Invoke-WebRequest o deixa muito lento
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Set-Location $PSScriptRoot

$pacote = Join-Path $PSScriptRoot "publicado\BibliotecaDigital"
$exe = Join-Path $pacote "biblioteca.exe"

function Passo($texto) { Write-Host ""; Write-Host "==> $texto" -ForegroundColor Cyan }
function Aviso($texto) { Write-Host $texto -ForegroundColor Yellow }

# Controle Inteligente de Aplicativos (Smart App Control). Ligado, ele recusa executável sem
# assinatura de autoridade reconhecida: o pacote da Release é assinado e passa; o compilado aqui não.
$sacLigado = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState -eq 1

# 1. Acervo. O configurar nunca baixa acervo de lugar nenhum: o acervo é da instituição, pode ter
#    material sensível, e só ela decide de onde ele vem (cópia, backup, repositório próprio).
Passo "Acervo"
$acervo = if ($env:BIBLIOTECA_ACERVO) { $env:BIBLIOTECA_ACERVO } else { Join-Path $PSScriptRoot "acervo" }
if (Test-Path (Join-Path $acervo "colecoes.json")) {
    Write-Host "ok ($acervo)"
} elseif ((Test-Path $acervo) -and (Get-ChildItem $acervo -Force | Select-Object -First 1)) {
    Aviso "A pasta $acervo tem arquivos, mas não tem colecoes.json. Confira se é mesmo o acervo."
} else {
    New-Item -ItemType Directory -Force $acervo | Out-Null
    Write-Host "criado vazio ($acervo)."
    Write-Host "Para trazer um acervo que já existe, copie-o para essa pasta. Para começar do zero, cadastre"
    Write-Host "uma coleção em Curadoria > Coleções e inclua os PDFs em Curadoria > Incluir."
}

# 2. Poppler. O pdftotext do Git é o do xpdf, sem -bbox-layout, e o LeitorPoppler o recusa:
#    por isso a busca é por um Poppler de verdade, nunca pelo PATH.
Passo "Poppler"
function EhPoppler($bin) { $bin -and (Test-Path (Join-Path $bin "pdftotext.exe")) -and (Test-Path (Join-Path $bin "pdftoppm.exe")) }
function AcharPoppler { Get-ChildItem $PastaPoppler -Recurse -Filter pdftotext.exe -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 | ForEach-Object { $_.DirectoryName } }
if (-not (EhPoppler $Poppler)) { $Poppler = $env:BIBLIOTECA_POPPLER }
if (-not (EhPoppler $Poppler)) { $Poppler = [Environment]::GetEnvironmentVariable("BIBLIOTECA_POPPLER", "User") }
if (-not (EhPoppler $Poppler)) { $Poppler = AcharPoppler }
if (EhPoppler $Poppler) {
    Write-Host "ok ($Poppler)"
} else {
    $versao = Invoke-RestMethod "https://api.github.com/repos/oschwartz10612/poppler-windows/releases/latest"
    $zipInfo = $versao.assets | Where-Object { $_.name -like "Release-*.zip" } | Select-Object -First 1
    if (-not $zipInfo) { throw "Não achei o zip do Poppler em $($versao.html_url)" }
    $zip = Join-Path $env:TEMP $zipInfo.name
    Write-Host "baixando $($zipInfo.name) ($([math]::Round($zipInfo.size / 1MB)) MB)..."
    Invoke-WebRequest $zipInfo.browser_download_url -OutFile $zip
    New-Item -ItemType Directory -Force $PastaPoppler | Out-Null
    Expand-Archive $zip -DestinationPath $PastaPoppler -Force
    Remove-Item $zip
    $Poppler = AcharPoppler
    if (-not (EhPoppler $Poppler)) { throw "Poppler extraído, mas pdftotext.exe não apareceu em $PastaPoppler" }
    Write-Host "instalado ($Poppler)"
}

# 3. Programa
function EmUso { Get-Process biblioteca -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } }
# O servidor roda sem janela; para trocar o exe, ele tem de parar. Rodando como serviço do
# Windows, só o administrador o para, e aí quem decide é a pessoa.
if ((Test-Path $exe) -and (EmUso)) {
    Write-Host "parando a Biblioteca Digital que está no ar, para poder atualizá-la..."
    try { EmUso | Stop-Process -Force -ErrorAction Stop; Start-Sleep -Seconds 1 }
    catch { throw "Não consegui parar a Biblioteca Digital (deve estar como serviço). Como administrador: sc.exe stop BibliotecaDigital; depois rode de novo." }
}

$release = $null
if (-not $Compilar) {
    Passo "Programa: pacote assinado de $RepositorioCodigo"
    try {
        $release = Invoke-RestMethod "https://api.github.com/repos/$RepositorioCodigo/releases/latest"
    } catch {
        Aviso "Nenhuma Release publicada ainda em $RepositorioCodigo; vou compilar aqui."
    }
    $zipInfo = if ($release) { $release.assets | Where-Object { $_.name -eq "BibliotecaDigital-win-x64.zip" } | Select-Object -First 1 }
    if ($release -and -not $zipInfo) { Aviso "A Release $($release.tag_name) não tem BibliotecaDigital-win-x64.zip; vou compilar aqui."; $release = $null }
}

if ($release) {
    $zip = Join-Path $env:TEMP "BibliotecaDigital-win-x64.zip"
    Write-Host "baixando $($release.tag_name) ($([math]::Round($zipInfo.size / 1MB)) MB)..."
    Invoke-WebRequest $zipInfo.browser_download_url -OutFile $zip
    New-Item -ItemType Directory -Force $pacote | Out-Null
    Expand-Archive $zip -DestinationPath $pacote -Force
    Remove-Item $zip
    $assinatura = Get-AuthenticodeSignature $exe
    if ($assinatura.Status -ne "Valid") { Aviso "Atenção: o biblioteca.exe baixado não tem assinatura válida ($($assinatura.Status))." }
    else { Write-Host "assinado por: $($assinatura.SignerCertificate.Subject)" }
    # O pacote leva o Poppler na própria pasta, para não depender de nada instalado na máquina.
    $destinoPoppler = Join-Path $pacote "poppler"
    New-Item -ItemType Directory -Force $destinoPoppler | Out-Null
    Copy-Item -Force (Join-Path $Poppler "*") $destinoPoppler
} else {
    Passo "Programa: compilando aqui"
    if ($sacLigado) {
        Aviso "O Controle Inteligente de Aplicativos está LIGADO: o exe compilado aqui vai ser bloqueado."
        Aviso "Use o pacote assinado (rode sem -Compilar) ou desligue-o em Segurança do Windows > Controle de aplicativos e do navegador."
    }
    $dotnetPadrao = "C:\Program Files\dotnet"
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path "$dotnetPadrao\dotnet.exe")) { $env:Path = "$dotnetPadrao;$env:Path" }
    $temSdk10 = (Get-Command dotnet -ErrorAction SilentlyContinue) -and [bool](dotnet --list-sdks | Where-Object { $_ -match '^10\.' })
    if (-not $temSdk10) {
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            throw "winget não encontrado. Instale o .NET 10 SDK à mão: https://dotnet.microsoft.com/download/dotnet/10.0"
        }
        Write-Host "instalando o .NET 10 SDK..."
        winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements --silent
        if ($LASTEXITCODE -ne 0) { throw "winget não conseguiu instalar o .NET 10 SDK (código $LASTEXITCODE)" }
        $env:Path = "$dotnetPadrao;$env:Path"   # o PATH novo só vale para janelas abertas depois
    }
    Write-Host ".NET $(dotnet --version)"
    # Para `dotnet run` e `dotnet test`; o pacote não precisa, leva o Poppler junto.
    [Environment]::SetEnvironmentVariable("BIBLIOTECA_POPPLER", $Poppler, "User")
    $env:BIBLIOTECA_POPPLER = $Poppler
    & (Join-Path $PSScriptRoot "publicar.ps1") -Poppler $Poppler
}

# 4. Atalho
Passo "Atalho"
if ($SemAtalho) {
    Write-Host "pulado (-SemAtalho)"
} else {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "Biblioteca Digital.lnk"))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $pacote
    $lnk.IconLocation = $exe
    $lnk.Description = "Abre a Biblioteca Digital (inicia o servidor, se preciso)"
    $lnk.Save()
    Write-Host "ok (Biblioteca Digital, na Área de Trabalho)"
}

# 5. Índice
Passo "Índice de busca"
$bloqueado = $sacLigado -and (Get-AuthenticodeSignature $exe).Status -ne "Valid"
if ($SemIndice) {
    Write-Host "pulado (-SemIndice)"
} elseif (Test-Path (Join-Path $PSScriptRoot "dados\busca.db")) {
    Write-Host "ok (dados\busca.db já existe; para refazer: biblioteca.exe reindexar)"
} elseif ($bloqueado) {
    Aviso "pulado: o Windows bloquearia o exe sem assinatura. Depois de resolver, rode: $exe reindexar"
} else {
    & $exe reindexar
    if ($LASTEXITCODE -ne 0) { throw "reindexar falhou (código $LASTEXITCODE)" }
}

Passo "Pronto"
if ($bloqueado) {
    Aviso "O exe foi compilado aqui e o Controle Inteligente de Aplicativos vai bloqueá-lo. Rode sem -Compilar para usar o pacote assinado."
} else {
    Write-Host "Abra pelo atalho 'Biblioteca Digital' na Área de Trabalho, ou: $exe"
}
