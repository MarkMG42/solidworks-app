[CmdletBinding()]
param(
    [string]$SolidWorksInteropPath = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist",
    [string]$OutputPath = ""
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $OutputPath) { $OutputPath = Join-Path $repo 'artifacts\SWToolsHub' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ($env:OS -ne 'Windows_NT') { throw 'Сборка пакета выполняется на Windows x64.' }
foreach ($name in @('SolidWorks.Interop.swpublished.dll', 'SolidWorks.Interop.sldworks.dll')) {
    if (-not (Test-Path (Join-Path $SolidWorksInteropPath $name))) { throw "Не найдена interop-сборка: $name. Укажите -SolidWorksInteropPath." }
}
# Не очищаем заданный каталог: он может содержать рабочие файлы пользователя.
if (Test-Path $OutputPath) { throw 'Каталог пакета уже существует. Укажите новый пустой путь через -OutputPath.' }
Push-Location (Join-Path $repo 'frontend')
try {
    & npm.cmd ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci завершился с ошибкой.' }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Сборка UI завершилась с ошибкой.' }
} finally { Pop-Location }
& dotnet build (Join-Path $repo 'addin\SWToolsHub.Addin.csproj') -c Release "-p:SolidWorksInteropPath=$SolidWorksInteropPath"
if ($LASTEXITCODE -ne 0) { throw 'Сборка дополнения завершилась с ошибкой.' }
$bin = Join-Path $repo 'addin\bin\Release\net48'
if (-not (Test-Path (Join-Path $bin 'SWToolsHub.Addin.dll'))) { throw 'Не найдена собранная DLL.' }
New-Item -ItemType Directory -Path $OutputPath | Out-Null
Copy-Item (Join-Path $bin '*') $OutputPath -Recurse
Copy-Item (Join-Path $repo 'frontend\dist') (Join-Path $OutputPath 'ui') -Recurse
New-Item -ItemType Directory -Path (Join-Path $OutputPath 'Library\Macros') -Force | Out-Null
Copy-Item (Join-Path $repo 'config\macros.demo.json') (Join-Path $OutputPath 'Library\macros.json')
Copy-Item (Join-Path $PSScriptRoot 'Install.ps1'),(Join-Path $PSScriptRoot 'Uninstall.ps1') $OutputPath
Copy-Item (Join-Path $repo 'УСТАНОВКА.txt') $OutputPath
if (-not (Get-ChildItem $OutputPath -Filter WebView2Loader.dll -Recurse)) { throw 'В пакете отсутствует WebView2Loader.dll.' }
Write-Host "Пакет подготовлен: $OutputPath. Рабочие .swp в пакет не включены."
