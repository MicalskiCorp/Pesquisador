# Gera a Biblioteca Digital para Windows: uma pasta com biblioteca.exe, que não exige .NET instalado,
# a tela, o modelo das coleções e o Poppler. Nada mais precisa ser instalado na máquina.
#
# Uso (na raiz do repositório):
#   .\publicar.ps1 -Poppler "C:\...\poppler-25.07.0\Library\bin" [-Atalho]
#
# Saída: .\publicado\BibliotecaDigital\ — dentro do repositório, o executável acha sozinho o acervo
# (.\acervo) e guarda o índice em .\dados. Copiado para outro lugar, ajuste appsettings.json.
param(
    [Parameter(Mandatory = $true)][string]$Poppler,
    [string]$Saida = ".\publicado\BibliotecaDigital",
    [switch]$Atalho
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path (Join-Path $Poppler "pdftotext.exe"))) { throw "pdftotext.exe não encontrado em $Poppler" }

dotnet publish src\BibliotecaDigital.App\BibliotecaDigital.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $Saida
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

$destinoPoppler = Join-Path $Saida "poppler"
New-Item -ItemType Directory -Force $destinoPoppler | Out-Null
Copy-Item -Force (Join-Path $Poppler "*") $destinoPoppler

$exe = (Resolve-Path (Join-Path $Saida "biblioteca.exe")).Path
if ($Atalho) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "Biblioteca Digital.lnk"))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = Split-Path $exe
    $lnk.IconLocation = $exe
    $lnk.Description = "Abre a Biblioteca Digital (inicia o servidor, se preciso)"
    $lnk.Save()
}

Write-Host ""
Write-Host "Pronto: $exe"
Write-Host "  Duplo-clique: abre a Biblioteca Digital no navegador (sobe o servidor se não estiver no ar)."
Write-Host "  Para iniciar com o Windows e atender a rede: biblioteca.exe servico-instalar (como administrador)."
Write-Host "  Primeira vez numa máquina nova: biblioteca.exe reindexar"
