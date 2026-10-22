!include "FileFunc.nsh"
!include "LogicLib.nsh"

Var UpdateProcessId

Function .onInit
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/UPDATEPID=" $UpdateProcessId
  ${If} ${Errors}
    StrCpy $UpdateProcessId ""
  ${EndIf}
FunctionEnd

Function SkipSelfUpdateDirectory
  ${If} $UpdateProcessId != ""
    ; /D already points at the running application's installation.
    Abort
  ${EndIf}
FunctionEnd

Function WaitForUpdatingApp
  ${If} $UpdateProcessId == ""
    Return
  ${EndIf}

  DetailPrint "Waiting for GitHub Downloader to exit..."
  System::Call 'kernel32::OpenProcess(i 0x100000, i 0, i $UpdateProcessId) p .r0 ?e'
  Pop $1
  ${If} $0 == 0
    ${If} $1 == 87
      ; ERROR_INVALID_PARAMETER: the process has already exited.
      Return
    ${EndIf}
    MessageBox MB_OK|MB_ICONSTOP "Could not wait for GitHub Downloader to exit. Quit the app from its tray menu and run setup again."
    Abort
  ${EndIf}

  System::Call 'kernel32::WaitForSingleObject(p r0, i 60000) i .r1'
  System::Call 'kernel32::CloseHandle(p r0)'
  ${If} $1 != 0
    MessageBox MB_OK|MB_ICONSTOP "GitHub Downloader has not exited. Quit it from its tray menu and run setup again. No application files have been replaced."
    Abort
  ${EndIf}
FunctionEnd

Function LaunchInstalledApp
  ; Use the desktop shell to open the app with the signed-in user's permissions.
  ExecShell "open" "$WINDIR\explorer.exe" '"$INSTDIR\${EXE}"'
FunctionEnd
