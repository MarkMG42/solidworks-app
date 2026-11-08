# Дополнение SW Tools Hub

`SWToolsHub.Addin.csproj`: C# COM Add-in, .NET Framework 4.8, x64, `ISwAddin`, Task Pane с WebView2. Сборка и установка: [docs/INSTALL.md](../docs/INSTALL.md). Краткая памятка: [УСТАНОВКА.txt](../УСТАНОВКА.txt).

Разработчик собирает пакет через `scripts/Build-Package.ps1` на Windows с SolidWorks 2025. Инженер устанавливает готовый пакет через `Install.ps1`, без Node.js и .NET SDK. Рабочие `.swp` передаются отдельно. Облако не подтверждает выполнение внутри SolidWorks: обязательная приёмка на Windows перечислена в инструкции.
