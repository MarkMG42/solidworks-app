# EXE-установщик

`SWToolsHub-Setup.exe` — тестовая неподписанная сборка, без рабочих макросов.
Node.js и .NET SDK не нужны инженеру. .NET Framework 4.8 требуется; WebView2 устанавливается bootstrapper Microsoft при отсутствии с проверкой подписи. При закрытом SolidWorks выполняется существующий Install.ps1. Отмена/ошибка блокирует успешное завершение.

Для воспроизводимой новой сборки на Windows: выполните `addin/scripts/Build-Package.ps1` с interop установленного SW2025, установите NSIS 3 и из папки installer выполните:

```
makensis /DPAYLOAD=C:\полный\путь\к\artifacts\SWToolsHub Setup.nsi
```

Текущий EXE подготовлен в Linux из net48 DLL с interop SW2024 32.1.0 и production UI. Это проверка компиляции, не подтверждение совместимости со SW2025. Windows-приёмка обязательна. NSIS и синтаксис PowerShell проверены; сам установщик в Windows не выполнялся. Публичный пакет не содержит .swp, CAD и секретов. Скачать EXE следует кнопкой Download raw file, а не сохранить HTML-страницу GitHub.
