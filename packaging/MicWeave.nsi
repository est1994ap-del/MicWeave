Unicode True
!include "MUI2.nsh"
!include "LogicLib.nsh"
Name "MicWeave - Development Preview"
!ifndef DEVELOPMENT_BUILD
!error "Public installer blocked pending device identity and release gates."
!endif
!ifndef PUBLISH_DIR
!error "Build with scripts/Build.ps1 -DevelopmentInstaller."
!endif
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\MicWeave"
InstallDirRegKey HKCU "Software\MicWeave" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
Icon "..\src\GameMusicShare.App\Assets\MicWeave.ico"
UninstallIcon "..\src\GameMusicShare.App\Assets\MicWeave.ico"
!define MUI_ICON "..\src\GameMusicShare.App\Assets\MicWeave.ico"
!define MUI_UNICON "..\src\GameMusicShare.App\Assets\MicWeave.ico"
!define MUI_WELCOMEPAGE_TEXT "Combine your microphone with music or other program audio. Select MicWeave Microphone in your calling or recording app.$\r$\n$\r$\nMicWeave keeps program sound playing on your normal speakers. VoiceMeeter is not required.$\r$\n$\r$\nFirst use installs a free, already-signed USB transport with administrator permission. Windows may need a restart. Secure Boot stays enabled."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\MicWeave.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open MicWeave"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "MicWeave"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"
  CreateShortcut "$DESKTOP\MicWeave.lnk" "$INSTDIR\MicWeave.exe" "" "$INSTDIR\Assets\MicWeave.ico"
  CreateShortcut "$SMPROGRAMS\MicWeave.lnk" "$INSTDIR\MicWeave.exe" "" "$INSTDIR\Assets\MicWeave.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\MicWeave" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave" "DisplayName" "MicWeave"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave" "DisplayVersion" "0.1.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave" "DisplayIcon" "$INSTDIR\Assets\MicWeave.ico"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$DESKTOP\MicWeave.lnk"
  Delete "$SMPROGRAMS\MicWeave.lnk"
  Delete "$INSTDIR\MicWeave.exe"
  Delete "$INSTDIR\MicWeave.pdb"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\THIRD_PARTY_NOTICES.md"
  Delete "$INSTDIR\docs\BUILDING.md"
  Delete "$INSTDIR\docs\DEVICE-IDENTITY.md"
  Delete "$INSTDIR\docs\IDENTITY-REQUEST.md"
  Delete "$INSTDIR\docs\RELEASING.md"
  Delete "$INSTDIR\docs\SECURITY-REVIEW.md"
  RMDir "$INSTDIR\docs"
  Delete "$INSTDIR\Assets\MicWeave.VirtualUsb.exe"
  Delete "$INSTDIR\Assets\MicWeave.UsbTransport.Setup.exe"
  Delete "$INSTDIR\Assets\MicWeave.ico"
  Delete "$INSTDIR\Assets\Fonts\OFL-Inter.txt"
  RMDir "$INSTDIR\Assets\Fonts"
  RMDir "$INSTDIR\Assets"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.Virtual-Cables.txt"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.usbip-win2.txt"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.NAudio.txt"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.Go.txt"
  Delete "$INSTDIR\ThirdPartyNotices\PATENTS.Go.txt"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.DotNet.txt"
  Delete "$INSTDIR\ThirdPartyNotices\THIRD-PARTY-NOTICES.DotNet.txt"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.WindowsDesktop.txt"
  Delete "$INSTDIR\ThirdPartyNotices\THIRD-PARTY-NOTICES.WindowsDesktop.txt"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.WindowsSDK.rtf"
  Delete "$INSTDIR\ThirdPartyNotices\LICENSE.CsWinRT.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR\ThirdPartyNotices"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave"
  DeleteRegKey HKCU "Software\MicWeave"
  ; Preserve user preferences and the shared signed USB transport. Other programs may use it.
SectionEnd
