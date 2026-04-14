; ============================================================
;  BM Smart Group Manager  Inno Setup Script
;  Revit 2023 Plugin  (per-user install)
; ============================================================

#define AppName      "BM Smart Group Manager"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "BuroMoscow"
#define AppURL       "https://buromoscow.ru"
#define RevitVer     "2023"
#define AddinTarget  "{userappdata}\Autodesk\Revit\Addins\2023"
#define BuildDir      SourcePath + "bin\Release"

[Setup]
AppId={{B1C2D3E4-F5A6-7890-BCDE-F01234567891}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
; Все файлы плагина прямо в папке Addins (DLL и .addin рядом)
DefaultDirName={#AddinTarget}
DefaultGroupName={#AppPublisher}
OutputDir={#SourcePath}Build\Installer
OutputBaseFilename=BM_GroupManager_v{#AppVersion}_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=yes
; Установка без прав администратора
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
UninstallDisplayName={#AppName}
MinVersion=6.1sp1
; Метаданные для снижения срабатываний антивирусов
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Installer
VersionInfoVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoProductName={#AppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Dirs]
Name: "{app}"; Permissions: users-full

[Files]
; DLL плагина — рядом с .addin
Source: "{#BuildDir}\BM_GroupManager.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"

[Code]
// ----------------------------------------------------------------
// После установки DLL создаём .addin файл с полным путём к DLL
// ----------------------------------------------------------------
procedure CurStepChanged(CurStep: TSetupStep);
var
  AddinFile : String;
  DllPath   : String;
  Lines     : TArrayOfString;
begin
  if CurStep <> ssPostInstall then Exit;

  AddinFile := ExpandConstant('{app}\BM_GroupManager.addin');
  DllPath   := ExpandConstant('{app}\BM_GroupManager.dll');

  SetArrayLength(Lines, 10);
  Lines[0] := '<?xml version="1.0" encoding="utf-8"?>';
  Lines[1] := '<RevitAddIns>';
  Lines[2] := '  <AddIn Type="Application">';
  Lines[3] := '    <Name>BM Smart Group Manager</Name>';
  Lines[4] := '    <Assembly>' + DllPath + '</Assembly>';
  Lines[5] := '    <AddInId>3A1B2C4D-5E6F-7A8B-9C0D-E1F234567890</AddInId>';
  Lines[6] := '    <FullClassName>BM_GroupManager.App</FullClassName>';
  Lines[7] := '    <VendorId>BM</VendorId>';
  Lines[8] := '    <VendorDescription>BuroMoscow</VendorDescription>';
  Lines[9] := '  </AddIn>';
  SaveStringsToFile(AddinFile, Lines, False);

  SetArrayLength(Lines, 1);
  Lines[0] := '</RevitAddIns>';
  SaveStringsToFile(AddinFile, Lines, True);
end;

// ----------------------------------------------------------------
// Проверяем наличие Revit 2023 перед установкой
// ----------------------------------------------------------------
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not DirExists('C:\Program Files\Autodesk\Revit 2023') then
  begin
    if MsgBox('Autodesk Revit 2023 не обнаружен на этом компьютере.' + #13#10 +
              'Продолжить установку?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

[UninstallDelete]
Type: files; Name: "{app}\BM_GroupManager.addin"
