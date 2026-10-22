!define APPNAME "Github-Downloader"
!define COMPANY "tobias2888"
!define EXE "Github-Downloader.exe"
!define VERSION "3.3.0"

Name "${APPNAME}"
OutFile "release-assets/${APPNAME}-Setup-arm64-${VERSION}.exe"

InstallDir "$PROGRAMFILES\${COMPANY}\${APPNAME}"
InstallDirRegKey HKCU "Software\${COMPANY}\${APPNAME}" ""

RequestExecutionLevel admin
!include "MUI2.nsh"
!include "windows-self-update.nsh"

!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipSelfUpdateDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Open GitHub Downloader"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchInstalledApp
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"

  Call WaitForUpdatingApp
  SetOutPath "$INSTDIR"

  ; copy entire publish directory recursively
  File /r "win-arm64\*"

  ; save install location in registry
  WriteRegStr HKCU "Software\${COMPANY}\${APPNAME}" "" $INSTDIR

  ; start menu shortcut
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXE}"

  ; desktop shortcut
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXE}"

  ; autostart for current user
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APPNAME}" '"$INSTDIR\${EXE}" --hidden'

  ; write uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; add to Windows uninstall list
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${COMPANY}"

SectionEnd


Section "Uninstall"

  ; remove shortcuts
  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"

  ; remove autostart
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APPNAME}"

  ; remove files and uninstall exe
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR"

  ; remove registry entries
  DeleteRegKey HKCU "Software\${COMPANY}\${APPNAME}"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"

SectionEnd
