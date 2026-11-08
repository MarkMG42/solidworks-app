$ErrorActionPreference = 'Stop'
try {
    $release = Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release
    if ($release -lt 528040) { throw 'Требуется .NET Framework 4.8 — компонент Windows, необходимый также SolidWorks 2025.' }
    $keys = @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
    $found = $false
    foreach ($key in $keys) {
        $version = (Get-ItemProperty $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($version -and $version -ne '0.0.0.0') { $found = $true }
    }
    if (-not $found) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $file = Join-Path $env:TEMP ('SWToolsHub-WebView2-' + [guid]::NewGuid() + '.exe')
        try {
            Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -UseBasicParsing -OutFile $file
            $signature = Get-AuthenticodeSignature $file
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Не подтверждена подпись Microsoft у установщика WebView2.' }
            $p = Start-Process $file -ArgumentList '/silent /install' -Wait -PassThru
            if ($p.ExitCode -ne 0) { throw "Не установлен WebView2: код $($p.ExitCode)." }
        } finally { Remove-Item $file -Force -ErrorAction SilentlyContinue }
    }
} catch { Write-Error $_; exit 1 }
