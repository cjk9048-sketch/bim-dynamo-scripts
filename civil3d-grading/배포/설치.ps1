# DH.Grading 애드인 설치 스크립트 (JACK 0723, 0728 갱신)
#   ① .bundle 폴더를 Civil3D 자동로드 위치로 복사
#   ② 한국 좌표계 정의(KOREA_GRS80/BESSEL ###TM + UTM-K, 9종)가 없으면 사용자 좌표계 사전에 설치(딱 한 번)
#      ※ v12.1부터 애드인 자체도 시작 시 좌표계를 검사·설치하므로(계정별 자동), 이 스크립트를 못 쓴
#        경우에도 번들 폴더만 복사돼 있으면 좌표계는 첫 실행 때 자동으로 채워진다.
# 사용법: 설치.bat 더블클릭(권장) 또는  powershell -ExecutionPolicy Bypass -File 설치.ps1

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
function Say($m, $c='White'){ Write-Host $m -ForegroundColor $c }

Say "==== DH.Grading 애드인 설치 ====" 'Cyan'

# ★★[JACK 0915 배포판] Civil3D가 켜져 있으면 <b>아무것도 안 하고 멈춘다</b>.
#   종전엔 경고만 하고 Enter를 받으면 그대로 복사했다. 그런데 robocopy는 잠긴 파일을
#   <b>조용히 건너뛴다</b> — 그러면 옛 DLL과 새 DLL이 섞인 채로 "복사 완료"가 뜬다.
#   exe 설치기는 이 '부분 설치'를 막으려고 잠김 사전검사를 넣어 뒀는데(0728),
#   같은 일을 하는 이 스크립트에는 그 처방이 안 퍼져 있었다.
if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
    Say "! Civil3D(acad.exe)가 실행 중입니다." 'Red'
    Say "  파일이 잠겨 <옛 것과 새 것이 섞인 채로> 설치될 수 있어 중단합니다." 'Yellow'
    Say "  Civil3D를 완전히 닫은 뒤 다시 실행해 주세요." 'Yellow'
    exit 1
}

# ── ① 번들 복사 ──────────────────────────────────────────────
$candidates = @(
    (Join-Path $scriptDir 'DH.Grading.bundle'),
    (Join-Path $scriptDir '..\src\DH.Grading.Civil\DH.Grading.bundle')
)
$bundleSrc = $candidates | Where-Object { Test-Path (Join-Path $_ 'PackageContents.xml') } | Select-Object -First 1
$pluginDir = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\DH.Grading.bundle'

if ($bundleSrc) {
    Say "번들 복사: $bundleSrc" 'Gray'
    Say "      → $pluginDir" 'Gray'
    # /E=하위폴더 포함 복사(삭제 안 함). 로그 최소화.
    $rc = robocopy $bundleSrc $pluginDir /E /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { Say "  번들 복사 실패(코드 $LASTEXITCODE) — Civil3D를 닫고 재시도하세요." 'Red' }
    else { Say "  번들 복사 완료" 'Green' }
} else {
    Say "번들 소스를 못 찾음 — 좌표계 정의만 확인/설치합니다." 'Yellow'
}

# ── ② 한국 좌표계 정의 설치(없을 때만) ──────────────────────────
$coordSrc = Join-Path $pluginDir 'Contents\coordsys'
if (-not (Test-Path (Join-Path $coordSrc 'Coordsys.CSD')) -and $bundleSrc) {
    $coordSrc = Join-Path $bundleSrc 'Contents\coordsys'
}
$srcCoord = Join-Path $coordSrc 'Coordsys.CSD'
$srcCat   = Join-Path $coordSrc 'Category.CSD'
$userCs    = Join-Path $env:LOCALAPPDATA 'Autodesk\User Geospatial Coordinate Systems'
$dstCoord  = Join-Path $userCs 'Coordsys.CSD'
$dstCat    = Join-Path $userCs 'Category.CSD'

if (-not (Test-Path $srcCoord)) {
    Say "좌표계 정의 파일을 못 찾음 — 좌표계 설치 생략." 'Yellow'
}
elseif (-not (Test-Path $dstCoord)) {
    # 사용자 좌표계 사전이 아예 없음 → 신규 설치(가장 흔한 경우)
    New-Item -ItemType Directory -Force $userCs | Out-Null
    Copy-Item $srcCoord $dstCoord -Force
    Copy-Item $srcCat   $dstCat   -Force
    Say "좌표계 정의 설치 완료(신규): KOREA_GRS80/BESSEL 125·127·129·131TM" 'Green'
}
else {
    # 사용자 사전이 이미 있음 → 한국 정의 포함 여부 확인(바이너리 ASCII 검색)
    $txt = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dstCoord))
    if ($txt.Contains('KOREA_GRS80')) {
        Say "좌표계 정의 이미 설치됨 — 생략." 'Green'
    } else {
        # 다른 사용자 정의가 있어 자동 병합 불가 → 덮어쓰지 않고 백업만(데이터 보호)
        Copy-Item $dstCoord "$dstCoord.dhbak" -Force
        Say "주의: 기존 사용자 좌표계 사전이 있어 자동 병합을 하지 않았습니다." 'Yellow'
        Say "      백업: $dstCoord.dhbak" 'Yellow'
        Say "      한국 좌표계가 필요하면 Civil3D에서 MAPCSLIBRARY 명령 → 가져오기로 아래 파일을 선택하세요:" 'Yellow'
        Say "      $coordSrc\CSLibrary.xml" 'Yellow'
    }
}

Say "==== 설치 종료 ====" 'Cyan'
Say "Civil3D를 실행하면 'DH 정지' 리본이 로드됩니다." 'Gray'
