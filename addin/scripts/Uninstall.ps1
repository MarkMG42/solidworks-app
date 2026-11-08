#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$InstallPath = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [Environment]::Is64BitProcess) { throw 'Используйте 64-разрядный Windows PowerShell.' }
if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) { throw 'Перед удалением закройте SolidWorks.' }
$dll = Join-Path $InstallPath 'SWToolsHub.Addin.dll'
if (-not (Test-Path $dll)) { throw 'Не найдена DLL; укажите -InstallPath с каталогом установки.' }
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" $dll /unregister /nologo
if ($LASTEXITCODE -ne 0) { throw 'Ошибка отмены COM-регистрации; файлы сохранены.' }
$guid = '{13B0B588-6E90-46B3-B76A-13DC794957A6}'
foreach ($key in @("HKLM:\SOFTWARE\SolidWorks\Addins\$guid", "HKCU:\Software\SolidWorks\AddInsStartup\$guid")) {
    if (Test-Path $key) { Remove-Item $key -Recurse }
}
Write-Host 'Регистрация удалена. Каталог приложения, библиотека макросов и настройки WebView2 сохранены.'
Write-Host "При необходимости удалите каталог $InstallPath вручную, предварительно сохранив Library."
