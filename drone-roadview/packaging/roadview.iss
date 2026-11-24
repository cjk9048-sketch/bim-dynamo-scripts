; 드론 로드뷰 — 설치 파일 (Inno Setup 6)
; 빌드: pyinstaller 다음에  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" packaging\roadview.iss
; 한국어 화면이라 이 파일은 UTF-8 BOM 으로 저장한다.
; 관리자 권한 없이 현재 사용자에게 설치 (회사 PC 에서 바로 깔리게).

#define AppName "드론 로드뷰"
#define AppVersion "1.0.10"
#define Publisher "DHEC Water and Sewage"
#define ExeName "드론로드뷰.exe"

[Setup]
AppId={{8C1D7A52-3E9B-4F27-9A61-5B0E2D7C4F13}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={localappdata}\Programs\DH.Roadview
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=드론로드뷰_설치_{#AppVersion}
SetupIconFile=roadview.ico
UninstallDisplayIcon={app}\{#ExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기"; GroupDescription: "바로가기:"

[InstallDelete]
; 옛 판 파일이 섞이지 않게 프로그램 폴더를 비우고 새로 넣는다 (사용자 자료는 %APPDATA%\DH.Roadview 에 있어 안 지워짐)
Type: filesandordirs; Name: "{app}\_internal"

[Files]
Source: "dist\드론로드뷰\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Messages]
WelcomeLabel2=[name] 을(를) 설치합니다.%n%nDJI 360 드론 영상(OSV) · 360 사진으로 로드뷰를 만들고, 상수도 관로(SHP · DXF)를 겹쳐 보는 프로그램입니다. 만든 로드뷰는 웹 주소(NAS)로 내보낼 수 있습니다.
FinishedLabel=설치를 마쳤습니다.%n%n바탕화면 또는 시작 메뉴의 [드론 로드뷰] 로 실행합니다.
