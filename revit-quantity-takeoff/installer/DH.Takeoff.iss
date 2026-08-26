; DH Takeoff - Revit 2026 add-in installer (Inno Setup 6)
; Build: dotnet build src\DH.Takeoff.sln -c Release   then   ISCC installer\DH.Takeoff.iss
; Installer UI strings are ASCII to compile without a UTF-8 BOM; the add-in UI itself is Korean.

#define AppName "DH Takeoff for Revit 2026"
#define AppVersion "0.34.0"
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
ArchitecturesInstallIn64BitMode=x64
OutputDir=Output
OutputBaseFilename=DH.Takeoff.Setup.{#RevitVer}-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}

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
