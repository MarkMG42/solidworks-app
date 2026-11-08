Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!ifndef PAYLOAD
!error "Specify /DPAYLOAD=package directory"
!endif
Name "SW Tools Hub (тестовая версия)"
OutFile "SWToolsHub-Setup.exe"
InstallDir "$PROGRAMFILES64\SWToolsHub"
RequestExecutionLevel admin
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "Russian"
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Требуется Windows x64 с SolidWorks 2025."
    Abort
  ${EndIf}
  MessageBox MB_OKCANCEL "Закройте SolidWorks перед установкой. Это тестовая сборка: запуск внутри SolidWorks ещё не подтверждён. Рабочие макросы передаются отдельно." IDOK continue
  Abort
  continue:
FunctionEnd
Section
  SetRegView 64
  ${DisableX64FSRedirection}
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\package"
  File /r "${PAYLOAD}\*.*"
  File "Prerequisites.ps1"
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\package\Prerequisites.ps1"'
  Pop $0
  StrCmp $0 "0" prereq_ok
  MessageBox MB_ICONSTOP "Не удалось подготовить компоненты. Проверьте Интернет и .NET Framework 4.8."
  Abort
  prereq_ok:
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\package\Install.ps1"'
  Pop $0
  StrCmp $0 "0" installed
  MessageBox MB_ICONSTOP "Ошибка установки. Подробности в журнале выше."
  Abort
  installed:
  MessageBox MB_OK "Установка завершена. Скопируйте оба .swp в C:\Program Files\SWToolsHub\Library\Macros. Затем SolidWorks → Сервис → Дополнения → SW Tools Hub."
SectionEnd
