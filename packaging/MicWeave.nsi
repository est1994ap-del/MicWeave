Unicode True
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinVer.nsh"
!include "x64.nsh"
!include "WordFunc.nsh"
!define PRODUCT_VERSION "0.1.0"
!ifndef DEVELOPMENT_BUILD
!error "Public installer blocked pending device identity and release gates."
!endif
!ifndef PAYLOAD_MANIFEST
!error "Build with scripts/Build.ps1 -DevelopmentInstaller."
!endif
!include "${PAYLOAD_MANIFEST}"
!ifdef TEST_ROOT
  !define APP_KEY "Software\MicWeave.InstallerTest"
  !define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave.InstallerTest"
  !define INSTALL_LOCATION "${TEST_ROOT}"
!else
  !define APP_KEY "Software\MicWeave"
  !define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave"
  !define INSTALL_LOCATION "$LOCALAPPDATA\Programs\MicWeave"
!endif
Name "MicWeave - Development Preview"
OutFile "${OUTPUT_FILE}"
InstallDir "${INSTALL_LOCATION}"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "0.1.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "MicWeave"
VIAddVersionKey /LANG=1033 "FileDescription" "MicWeave development installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "MicWeave contributors"
Icon "..\src\GameMusicShare.App\Assets\MicWeave.ico"
UninstallIcon "..\src\GameMusicShare.App\Assets\MicWeave.ico"
!define MUI_ICON "..\src\GameMusicShare.App\Assets\MicWeave.ico"
!define MUI_UNICON "..\src\GameMusicShare.App\Assets\MicWeave.ico"
!define MUI_WELCOMEPAGE_TEXT "Combine your microphone with music or other program audio. Select MicWeave Microphone in your calling or recording app.$\r$\n$\r$\nYour normal speakers keep working. VoiceMeeter is not required. No driver-signing account or certificate is needed.$\r$\n$\r$\nFirst use may install the free, already-signed USBip component with administrator approval. Save work first: USB devices can reconnect and Windows may need a restart. Secure Boot stays enabled.$\r$\n$\r$\nThis is a development preview, not a generally tested release."
!insertmacro MUI_PAGE_WELCOME
; Fixed per-user location avoids writing into an unrelated shared folder.
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp
!define MUI_FINISHPAGE_RUN_TEXT "Open MicWeave"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Var AppMutex

!macro AcquireAppMutex PREFIX
Function ${PREFIX}AcquireAppMutex
  retry:
  System::Call 'kernel32::CreateMutexW(p0, i0, w "Local\GameMusicShare.Standalone") p.r0 ?e'
  Pop $1
  StrCpy $AppMutex $0
  ${If} $0 = 0
  ${OrIf} $1 = 183
    ${If} $0 <> 0
      System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
    StrCpy $AppMutex 0
    IfSilent busy
    MessageBox MB_RETRYCANCEL|MB_ICONINFORMATION "Close MicWeave before continuing. Setup will not stop your microphone or interrupt your call automatically." IDRETRY retry
    busy:
    SetErrorLevel 1618
    Quit
  ${EndIf}
FunctionEnd
!macroend
!insertmacro AcquireAppMutex ""
!insertmacro AcquireAppMutex "un."

Function .onInit
  SetShellVarContext current
  ; Ignore /D and stale registry locations; only this app-owned location is writable.
  StrCpy $INSTDIR "${INSTALL_LOCATION}"
  ${IfNot} ${AtLeastWin11}
    MessageBox MB_OK|MB_ICONSTOP "MicWeave requires Windows 11." /SD IDOK
    SetErrorLevel 1633
    Quit
  ${EndIf}
  ${If} ${IsNativeAMD64}
  ${ElseIf} ${IsNativeARM64}
    !ifndef ARM64_PUBLISH_DIR
      MessageBox MB_OK|MB_ICONSTOP "This package is for Intel/AMD PCs. Use a MicWeave installer that includes ARM64 support. No driver was installed." /SD IDOK
      SetErrorLevel 1633
      Quit
    !endif
  ${Else}
    SetErrorLevel 1633
    Quit
  ${EndIf}
  Call AcquireAppMutex
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "DisplayVersion"
  ${VersionCompare} $0 "${PRODUCT_VERSION}" $1
  ${If} $1 = 1
    MessageBox MB_OK|MB_ICONSTOP "A newer MicWeave version is installed. This older installer will not overwrite it." /SD IDOK
    SetErrorLevel 1638
    Quit
  ${EndIf}
FunctionEnd

Function LaunchApp
  System::Call 'kernel32::CloseHandle(p $AppMutex)'
  StrCpy $AppMutex 0
  ExecShell "open" "$INSTDIR\MicWeave.exe"
FunctionEnd

Function un.onInit
  SetShellVarContext current
  GetFullPathName $0 "$INSTDIR"
  GetFullPathName $1 "${INSTALL_LOCATION}"
  ${If} $0 != $1
    MessageBox MB_OK|MB_ICONSTOP "MicWeave's uninstaller is outside its installation folder. No files will be removed." /SD IDOK
    SetErrorLevel 1603
    Quit
  ${EndIf}
  Call un.AcquireAppMutex
FunctionEnd

Section "MicWeave"
  SetOutPath "$INSTDIR"
  ClearErrors
  !ifdef ARM64_PUBLISH_DIR
    ${If} ${IsNativeARM64}
      File /r "${ARM64_PUBLISH_DIR}\*.*"
    ${Else}
      File /r "${PUBLISH_DIR}\*.*"
    ${EndIf}
  !else
    File /r "${PUBLISH_DIR}\*.*"
  !endif
  IfErrors install_failed
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors install_failed
  !ifndef TEST_ROOT
    CreateShortcut "$DESKTOP\MicWeave.lnk" "$INSTDIR\MicWeave.exe" "" "$INSTDIR\Assets\MicWeave.ico"
    CreateShortcut "$SMPROGRAMS\MicWeave.lnk" "$INSTDIR\MicWeave.exe" "" "$INSTDIR\Assets\MicWeave.ico"
  !endif
  WriteRegStr HKCU "${APP_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "MicWeave - Development Preview"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Assets\MicWeave.ico"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/est1994ap-del/MicWeave"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  Goto install_done
  install_failed:
    SetErrorLevel 1603
    Abort "MicWeave could not copy all its files. Close programs using this installation and run setup again."
  install_done:
SectionEnd

Section "Uninstall"
  ClearErrors
  !insertmacro RemoveOwnedPayload
  IfErrors uninstall_failed
  !ifndef TEST_ROOT
    Delete "$DESKTOP\MicWeave.lnk"
    Delete "$SMPROGRAMS\MicWeave.lnk"
  !endif
  !insertmacro RemoveEmptyPayloadDirectories
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "${APP_KEY}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ; No recursive deletes. Preserve unknown files, preferences and shared USBip.
  Goto uninstall_done
  uninstall_failed:
    SetErrorLevel 1603
    Abort "Some MicWeave files are still in use. Close programs using them and run Uninstall again. The Installed Apps entry was kept so you can retry."
  uninstall_done:
SectionEnd
