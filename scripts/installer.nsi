; One double-click setup for guild members.
; Installs per user (no administrator prompt) and leaves the upload token alone on uninstall.
; The token lives in %AppData%\GuildSyncCompanion, which is not this folder.

Unicode True
ManifestDPIAware true

!include "MUI2.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "0.1.6"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\dist\GuildSyncCompanion-Setup.exe"
!endif
!ifndef PAYLOAD
  !define PAYLOAD "..\dist\win-x64"
!endif
!ifndef ICON
  !define ICON "..\src\GuildSync.Companion\Assets\crest.ico"
!endif

!define APPNAME "GuildSync Companion"
!define EXE "GuildSyncCompanion.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\GuildSync Companion"
!define RUNKEY "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APPNAME}"
Caption "${APPNAME} Setup"
OutFile "${OUTFILE}"
Icon "${ICON}"
InstallDir "$LOCALAPPDATA\GuildSync Companion"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails nevershow
ShowUninstDetails nevershow
BrandingText "Twilight Tavern"

VIProductVersion "${APP_VERSION}.0"
VIFileVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "CompanyName" "Twilight Tavern"
VIAddVersionKey "FileDescription" "Setup for GuildSync Companion"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "Copyright © 2026 Fifthdread and contributors. Not affiliated with Blizzard Entertainment."

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "GuildSync Companion"
!define MUI_WELCOMEPAGE_TEXT "This puts the Twilight Tavern sync tool on your PC.$\r$\n$\r$\nClick Install. The app opens when it finishes. Paste the upload token from twilighttavern.co/upload (log in with Discord on that page). It finds World of Warcraft and installs the guild addon.$\r$\n$\r$\nClosing the window leaves it running beside the clock. Your character data keeps syncing."
!define MUI_FINISHPAGE_TITLE "Ready to paste your token"
!define MUI_FINISHPAGE_TEXT "GuildSync Companion is on this PC.$\r$\n$\r$\nClick Finish and it will open. Paste the token from twilighttavern.co/upload. A shortcut is on the desktop and in the Start menu if you need it again."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_PARAMETERS "--show"
!define MUI_FINISHPAGE_RUN_TEXT "Open GuildSync Companion now"
!define MUI_FINISHPAGE_CANCEL_ENABLED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

!insertmacro GetSize

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "GuildSync Companion needs 64-bit Windows."
    Abort
  ${EndIf}
FunctionEnd

Section "Install"
  ; A running copy locks its own files. Missing process is fine.
  nsExec::Exec "taskkill /F /IM $\"${EXE}$\""
  Pop $0
  ClearErrors

  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0 SW_SHOWNORMAL "" "Sync characters to Twilight Tavern"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0 SW_SHOWNORMAL "" "Sync characters to Twilight Tavern"

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "Twilight Tavern"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"

  ; Silent installs are the in-app updater. Open the window, not only the tray icon.
  IfSilent silent_launch end_launch
  silent_launch:
    Exec '"$INSTDIR\${EXE}" --show'
  end_launch:
SectionEnd

Section "Uninstall"
  nsExec::Exec "taskkill /F /IM $\"${EXE}$\""
  Pop $0
  ClearErrors

  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"

  DeleteRegValue HKCU "${RUNKEY}" "${APPNAME}"
  DeleteRegKey HKCU "${UNINSTKEY}"

  ; Upload token stays in %AppData%\GuildSyncCompanion.
  RMDir /r "$INSTDIR"
SectionEnd
