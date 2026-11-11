Unicode True
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"
!include "x64.nsh"

!if "${PRODUCT}" == "SpectraVision-Test"
!define PRODUCT_NAME "RAM HAX"
; Keep the staging and stable Start menu entries from replacing each other.
!define START_MENU_DIR "$SMPROGRAMS\SpectraVision Staging"
!else if "${PRODUCT}" == "SpectraVision-Local"
!define PRODUCT_NAME "RAM HAX Local"
!define START_MENU_DIR "$SMPROGRAMS"
!else
!define PRODUCT_NAME "RAM HAX"
!define START_MENU_DIR "$SMPROGRAMS"
!endif
Name "${PRODUCT_NAME}"
OutFile "${SETUP_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT}"
InstallDirRegKey HKCU "Software\${PRODUCT}" "InstallDir"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "${PRODUCT_NAME}"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\versions\${VERSION}\ESPWorkbench.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "RAM HAX requires 64-bit Windows." /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
  ReadRegStr $0 HKCU "Software\${PRODUCT}" "Version"
  ${If} $0 != ""
    ${VersionCompare} "$0" "${VERSION}" $1
    ${If} $1 == 1
      MessageBox MB_ICONSTOP "A newer version of RAM HAX is already installed." /SD IDOK
      SetErrorLevel 2
      Abort
    ${EndIf}
  ${EndIf}
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\ESPWorkbench.UserInstance") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::WaitForSingleObject(p r0, i 10000) i.r1'
    System::Call 'kernel32::CloseHandle(p r0)'
    ${If} $1 != 0
    ${AndIf} $1 != 128
      MessageBox MB_ICONSTOP "Close RAM HAX and run Setup again." /SD IDOK
      SetErrorLevel 2
      Abort
    ${EndIf}
  ${EndIf}
FunctionEnd

Section "RAM HAX"
  SetShellVarContext current
  SetOutPath "$INSTDIR\versions\${VERSION}"
  SetOverwrite on
  File "${PAYLOAD}\ESPWorkbench.exe"
  File "${PAYLOAD}\README.ar.md"
  SetOutPath "$INSTDIR\versions\${VERSION}\native"
  File "${PAYLOAD}\native\esp-reader"
  SetOutPath "$INSTDIR\versions\${VERSION}\profiles"
  File "${PAYLOAD}\profiles\*.json"
  SetOutPath "$INSTDIR\versions\${VERSION}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  Delete "$SMPROGRAMS\${PRODUCT}.lnk"
!if "${PRODUCT}" == "SpectraVision-Test"
  Delete "$SMPROGRAMS\RAM HAX Test.lnk"
  CreateDirectory "${START_MENU_DIR}"
!endif
  CreateShortcut "${START_MENU_DIR}\${PRODUCT_NAME}.lnk" "$INSTDIR\versions\${VERSION}\ESPWorkbench.exe"

  ; The shared desktop name can be replaced by another install. Record this file
  ; so this uninstaller only removes the shortcut it actually created.
  ClearErrors
  CreateShortcut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\versions\${VERSION}\ESPWorkbench.exe"
  ${IfNot} ${Errors}
    GetFileTime "$DESKTOP\${PRODUCT_NAME}.lnk" $0 $1
    ${IfNot} ${Errors}
      WriteRegStr HKCU "Software\${PRODUCT}" "DesktopShortcutStamp" "$0:$1"
    ${EndIf}
  ${EndIf}
  WriteRegStr HKCU "Software\${PRODUCT}" "Version" "${VERSION}"
  WriteRegStr HKCU "Software\${PRODUCT}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "NoRepair" 1
SectionEnd

Function .onInstSuccess
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/UPDATE" $1
  IfErrors done
  Exec '"$INSTDIR\versions\${VERSION}\ESPWorkbench.exe" --restart-after-update'
  done:
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\${PRODUCT}.lnk"
  Delete "${START_MENU_DIR}\${PRODUCT_NAME}.lnk"
!if "${PRODUCT}" == "SpectraVision-Test"
  RMDir "${START_MENU_DIR}"
!endif
  ReadRegStr $2 HKCU "Software\${PRODUCT}" "DesktopShortcutStamp"
  ${If} $2 != ""
    ClearErrors
    GetFileTime "$DESKTOP\${PRODUCT_NAME}.lnk" $0 $1
    ${IfNot} ${Errors}
    ${AndIf} $2 == "$0:$1"
      Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
    ${EndIf}
  ${EndIf}
  RMDir /r "$INSTDIR\versions"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}"
  DeleteRegKey HKCU "Software\${PRODUCT}"
SectionEnd
