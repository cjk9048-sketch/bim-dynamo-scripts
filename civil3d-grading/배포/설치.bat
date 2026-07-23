@echo off
chcp 65001 >nul
rem DH.Grading 애드인 설치 — 이 파일을 더블클릭하세요.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0설치.ps1"
echo.
pause
