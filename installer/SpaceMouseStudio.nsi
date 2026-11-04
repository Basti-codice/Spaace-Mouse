; Space Mouse Studio installer (NSIS 3, Modern UI 2). Per-user install: no administrator rights needed.
; Build: makensis -DVERSION=1.0.0 -DSRCDIR=<publish dir> -DOUTFILE=<setup.exe> SpaceMouseStudio.nsi
Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef SRCDIR
  !error "Define SRCDIR (folder containing SpaceMouseStudio.exe)"
!endif
!ifndef OUTFILE
  !define OUTFILE "SpaceMouseStudio-${VERSION}-setup.exe"
!endif

!define APPNAME "Space Mouse Studio"
!define EXE "SpaceMouseStudio.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpaceMouseStudio"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\SpaceMouseStudio"
InstallDirRegKey HKCU "Software\SpaceMouseStudio" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${VERSION}"
!define MUI_ICON "${SRCDIR}\..\..\src\SpaceMouse.App\Assets\app.ico"
!define MUI_UNICON "${SRCDIR}\..\..\src\SpaceMouse.App\Assets\app.ico"
!define MUI_ABORTWARNING

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APPNAME}"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Independent project; not affiliated with 3Dconnexion"

Function .onInit
  ; Close a running instance so files can be replaced.
  nsExec::Exec 'taskkill /IM ${EXE} /F'
  Pop $0
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"
  File "${SRCDIR}\${EXE}"
  File /nonfatal "${SRCDIR}\README.txt"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXE}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXE}"

  WriteRegStr HKCU "Software\SpaceMouseStudio" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "Space Mouse Studio"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /IM ${EXE} /F'
  Pop $0
  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"
  ; autostart entry written by the app
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "SpaceMouseStudio"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\SpaceMouseStudio"
  ; User data in %APPDATA%\SpaceMouseStudio is intentionally kept (profiles, calibration).
SectionEnd
