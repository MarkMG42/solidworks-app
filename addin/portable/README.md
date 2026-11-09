# SW Tools Hub Portable — разработчику

Актуально на 9 ноября 2026. Полный контекст: [HANDOFF](../../docs/HANDOFF.md).

## Архитектура

WinForms, .NET Framework 4.8 x64, manifest asInvoker. Без COM-регистрации, WebView2, серверов, БД, Node/SDK у пользователя. GetActiveObject подключается к открытому SolidWorks; GetMacroMethods и RunMacro2 вызываются из главного STA UI-потока.

- `Program.cs`: UI, viewport, адаптивная разметка, фильтры/избранное, настройки, запуск. ▶ передаёт Macro своей строки напрямую в RunSelected; не возвращать скрытый ListBox для выбора.
- `UiTheme.cs`: палитра, приватные Bold/Regular TTF, скругления, задержка hover 180 ms, переход около 150 ms, рост круглой кнопки и значка на 20%.
- `EntryDialog.cs`: модуль/процедура, GetMacroMethods, запасная точка, ручной ввод и сохранение по пути. Клиентская область 640 × 520.
- `EntryComboBox.cs`: наследник стандартного редактируемого ComboBox. Native popup/ввод/клавиатура сохранены; изменены рамка, отступ редактора и стрелка. OnDropDown/OnDropDownClosed меняют направление; стрелка примерно в 24 логических px от правого края.
- `MacroFolder.cs`: реальные `.swp` в корне и папках Избранное/Чертежи/Сборки/Лазер, плюс старая Детали. Никакой рекурсии и автоматического перемещения файлов.
- `MacroEntry.cs`: разбор COM-массивов/строк, точек входа и дубликатов.
- `FolderPicker.cs`: системный Common Item Dialog с адресной строкой.
- `%LOCALAPPDATA%\SWToolsHub\portable.json`: Folder, Entries, Favorites. Ключи — полные пути.

Избранное — отметки в настройках, не копии `.swp`. Сама папка Избранное не делает помещённые файлы избранными автоматически. Пути и настройки старой библиотеки сохраняются.

## Сборка

```powershell
dotnet build addin/portable/SWToolsHub.Portable.csproj -c Release -p:SolidWorksInteropPath="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
dotnet run --project addin/tests/folder/Folder.Checks.csproj
python installer/build-portable.py
git diff --check
```

После любой правки приложения пересоздавать ZIP и SHA-256. В ZIP нужны EXE, EXE.config, interop DLL, Bold/Regular TTF, лицензия, инструкция и пустые папки библиотеки. Рабочие макросы/CAD-файлы, bin/obj и секреты в Git не добавлять.

В облаке .NET SDK 8.0.427 и interop NuGet 32.1.0 (API 2024) дают проверку компиляции, не приёмку SW2025. 27 проверок сканера/разбора процедур не исполняют VBA.

## Проверка на Windows

Проверить круг/hover/DPI при изменении размеров, скролл и доступность последнего макроса; прямой play без предварительного выбора; GetMacroMethods и ручной ввод; смену модуля и списка процедур, новую стрелку мышью/F4/Alt+Down/Escape, нежирный текст; отмену; отсутствие SW/документа/файла; оба известных макроса на копиях чертежей. Облачная среда эти сценарии не исполняет.

## Превью

`docs/preview/` — браузерный макет с демонстрационными данными. Он не исполняет COM/VBA и не является снимком WinForms. Сначала показывать макет в чате, затем менять программу по команде пользователя. Последнее изменение Regular/стрелок утверждено и включено в итоговый архив.
