@echo off
rem DH.Grading add-in installer - double-click this file.
rem ASCII only + CRLF on purpose: a UTF-8 Korean .bat with LF line ends breaks cmd (1123 test: 'rshell' is not recognized).
rem Finds the one .ps1 next to this file (its name is Korean, so it is not written here).
set "PS1="
for %%F in ("%~dp0*.ps1") do set "PS1=%%~fF"
if not defined PS1 (
  echo [DH] install script ^(.ps1^) not found next to this file.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
echo.
pause
