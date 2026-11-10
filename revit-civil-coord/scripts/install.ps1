# DH Civil 좌표 맞춤 — Revit 2026 설치(빌드한 DLL 둘 + 매니페스트).
# 쓰는 법: powershell -ExecutionPolicy Bypass -File scripts\install.ps1   (Release 빌드 뒤)
# 이미 설치돼 있으면 덮기 전에 %APPDATA%\DH.CivilCoord\backup\<시각>\ 에 옛 것을 둔다. 끝에 해시를 대조한다.
# Revit 이 그 DLL 을 쓰고 있으면(켜 둔 채 다시 설치) 복사가 막힌다 — Revit 을 닫고 다시.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src\DH.CivilCoord.Revit\bin\Release\net8.0-windows'
$addins = 'C:\ProgramData\Autodesk\Revit\Addins\2026'
$target = Join-Path $addins 'DH.CivilCoord'
$manifest = Join-Path $root 'installer\DH.CivilCoord.addin'
$files = @('DH.CivilCoord.Revit.dll', 'DH.CivilCoord.Core.dll')

foreach ($f in $files) { if (-not (Test-Path -LiteralPath (Join-Path $src $f))) { throw "빌드 결과가 없음: $f — 먼저 dotnet build -c Release" } }

if (Test-Path -LiteralPath $target) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backup = Join-Path $env:APPDATA "DH.CivilCoord\backup\$stamp"
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Copy-Item -Path (Join-Path $target '*') -Destination $backup -Force
    $old = Join-Path $addins 'DH.CivilCoord.addin'
    if (Test-Path -LiteralPath $old) { Copy-Item -LiteralPath $old -Destination $backup -Force }
    Write-Output "백업: $backup"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($f in $files) { Copy-Item -LiteralPath (Join-Path $src $f) -Destination $target -Force }
Copy-Item -LiteralPath $manifest -Destination $addins -Force

foreach ($f in $files) {
    $a = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $src $f)).Hash
    $b = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $target $f)).Hash
    if ($a -ne $b) { throw "해시 다름: $f" }
    Write-Output ("{0}  {1}" -f $f, $b.Substring(0, 8))
}
Write-Output "설치 끝: $target"
