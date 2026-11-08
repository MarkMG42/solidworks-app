#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$InstallPath = "$env:ProgramFiles\SWToolsHub")
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [Environment]::Is64BitProcess) { throw 'Используйте 64-разрядный Windows PowerShell.' }
if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) { throw 'Перед установкой закройте SolidWorks.' }
$source = $PSScriptRoot
foreach ($relative in @('SWToolsHub.Addin.dll', 'ui\index.html', 'Library\macros.json')) {
    if (-not (Test-Path (Join-Path $source $relative))) { throw "Неполный пакет: $relative. Запускайте Install.ps1 из собранного пакета." }
}
# Реальную доступность WebView2 проверяет дополнение; установка Runtime описана в инструкции.
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
if ($InstallPath -eq [IO.Path]::GetPathRoot($InstallPath)) { throw 'Нельзя устанавливать в корень диска.' }
if ($InstallPath.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Каталог установки не должен находиться внутри исходного пакета.' }
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
if ($source.TrimEnd('\') -ne $InstallPath.TrimEnd('\')) {
    # Копируем файлы по относительным путям: повторная установка не создаёт ui\ui.
    Get-ChildItem $source -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($source.TrimEnd('\').Length + 1)
        if (-not $relative.StartsWith('Library\', [StringComparison]::OrdinalIgnoreCase)) {
            $destination = Join-Path $InstallPath $relative
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }
    }
    New-Item -ItemType Directory -Path (Join-Path $InstallPath 'Library\Macros') -Force | Out-Null
    if (-not (Test-Path (Join-Path $InstallPath 'Library\macros.json'))) {
        Copy-Item (Join-Path $source 'Library\macros.json') (Join-Path $InstallPath 'Library\macros.json')
    }
}
$regasm = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if (-not (Test-Path $regasm)) { throw 'Установите .NET Framework 4.8.' }
& $regasm (Join-Path $InstallPath 'SWToolsHub.Addin.dll') /codebase /nologo
if ($LASTEXITCODE -ne 0) { throw 'Ошибка COM-регистрации.' }
$guid = '{13B0B588-6E90-46B3-B76A-13DC794957A6}'
$key = "HKLM:\SOFTWARE\SolidWorks\Addins\$guid"
New-Item $key -Force | Out-Null
(Get-Item $key).SetValue('', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
New-ItemProperty $key -Name Title -Value 'SW Tools Hub' -PropertyType String -Force | Out-Null
New-ItemProperty $key -Name Description -Value 'Локальная библиотека VBA-макросов' -PropertyType String -Force | Out-Null
# HKCU относится к пользователю, запустившему эту административную консоль.
$startup = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
New-Item $startup -Force | Out-Null
(Get-Item $startup).SetValue('', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
Write-Host "Установлено в $InstallPath. Скопируйте .swp в Library\Macros и включите SW Tools Hub в SolidWorks: Сервис → Дополнения."
