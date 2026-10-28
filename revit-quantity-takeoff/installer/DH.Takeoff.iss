; DH Takeoff - Revit 2026 add-in installer (Inno Setup 6)
; Build: dotnet build src\DH.Takeoff.sln -c Release   then   ISCC installer\DH.Takeoff.iss
; Installer UI strings are ASCII to compile without a UTF-8 BOM; the add-in UI itself is Korean.

#define AppName "DH Takeoff for Revit 2026"
; Version 0.NN.0 = add-in release RNN (0.67.0 = R67, 2026-10-27: formwork plates first, auto/user mode, joint rules).
; 0.67.1 = R67a, 2026-10-28: 3D length numbers on every plate of a line (no 60-plate cap), overlapping numbers hidden per view.
#define AppVersion "0.67.1"
#define Publisher "DHEC Water and Sewage"
#define RevitVer "2026"
#define SrcDir "..\src\DH.Takeoff.Revit\bin\Release\net8.0-windows"
#define AddinsRoot "{commonappdata}\Autodesk\Revit\Addins\" + RevitVer

[Setup]
AppId={{8C2B6A14-3D55-4E77-9A21-DH202600TAKE}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={#AddinsRoot}\DH.Takeoff
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=DH.Takeoff.Setup.{#RevitVer}-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
; Revit holds the DLLs while running - ask to close it instead of failing on locked files.
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Files]
; Add-in DLLs -> Addins\2026\DH.Takeoff\
Source: "{#SrcDir}\DH.Takeoff.Revit.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\DH.Takeoff.Core.dll";  DestDir: "{app}"; Flags: ignoreversion
; Manifest -> Addins\2026\  (Revit scans this folder for *.addin)
Source: "DH.Takeoff.addin"; DestDir: "{#AddinsRoot}"; Flags: ignoreversion

[UninstallDelete]
Type: files;          Name: "{#AddinsRoot}\DH.Takeoff.addin"
Type: filesandordirs; Name: "{app}"

[Messages]
WelcomeLabel2=This will install [name] into the Revit {#RevitVer} add-ins folder for all users.%n%nClose Revit before continuing.
