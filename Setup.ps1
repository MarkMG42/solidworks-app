[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
try {
    if (-not [Environment]::Is64BitProcess) { throw 'Запустите установку в 64-разрядной Windows.' }
    if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) { throw 'Закройте SolidWorks и повторите установку.' }
    foreach ($tool in @('node.exe','npm.cmd','dotnet.exe')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "Не найден $tool. Для первой сборки установите Node.js 22.12+ и .NET SDK 8+. Затем запустите этот файл снова. Подробности: docs/INSTALL.md."
        }
    }
    $interop = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'
    if (-not (Test-Path (Join-Path $interop 'SolidWorks.Interop.sldworks.dll'))) {
        Add-Type -AssemblyName System.Windows.Forms
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Выберите папку api\redist установленного SolidWorks 2025'
        if ($picker.ShowDialog() -ne 'OK') { throw 'Установка отменена.' }
        $interop = $picker.SelectedPath
        $picker.Dispose()
    }
    $output = Join-Path $PSScriptRoot ('artifacts\SWToolsHub-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    & (Join-Path $PSScriptRoot 'addin\scripts\Build-Package.ps1') -SolidWorksInteropPath $interop -OutputPath $output
    $install = Join-Path $output 'Install.ps1'
    # RunAs показывает стандартное окно UAC; отмена не считается успешной установкой.
    $process = Start-Process "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -Wait -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $install + '"'))
    if ($process.ExitCode -ne 0) { throw "Установка завершилась с ошибкой: $($process.ExitCode)." }
    Write-Host 'Готово. Передайте оба .swp в C:\Program Files\SWToolsHub\Library\Macros.'
    Write-Host 'Откройте SolidWorks → Сервис → Дополнения → SW Tools Hub.'
    Start-Process explorer.exe -ArgumentList ('"' + "$env:ProgramFiles\SWToolsHub\Library\Macros" + '"')
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
