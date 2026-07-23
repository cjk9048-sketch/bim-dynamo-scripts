# DH.Grading 애드인 설치 스크립트 (JACK 0723)
#   ① .bundle 폴더를 Civil3D 자동로드 위치로 복사
#   ② 한국 좌표계 정의(KOREA_GRS80/BESSEL ###TM)가 없으면 사용자 좌표계 사전에 설치(딱 한 번)
# 사용법: 설치.bat 더블클릭(권장) 또는  powershell -ExecutionPolicy Bypass -File 설치.ps1

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
function Say($m, $c='White'){ Write-Host $m -ForegroundColor $c }

Say "==== DH.Grading 애드인 설치 ====" 'Cyan'

# Civil3D/AutoCAD 실행 중이면 DLL이 잠겨 복사 실패 → 안내
if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
    Say "! Civil3D(acad.exe)가 실행 중입니다. 완전히 닫은 뒤 다시 실행하세요." 'Yellow'
    Read-Host "Enter를 누르면 계속(복사 실패할 수 있음), 창을 닫으면 중단"
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
        Say "      백업: $dstCoord.dhbak — 한국 좌표계가 필요하면 수동 확인이 필요합니다." 'Yellow'
    }
}

Say "==== 설치 종료 ====" 'Cyan'
Say "Civil3D를 실행하면 'DH 정지' 리본이 로드됩니다." 'Gray'
