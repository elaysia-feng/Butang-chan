@echo off
chcp 65001 >nul
cd /d "%~dp0"

rem 桌宠本体在 app 目录；没编译过就先编译。
if not exist "app\ButangPet.exe" (
  echo 首次运行，正在编译...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build.ps1"
  if errorlevel 1 (
    echo.
    echo 编译失败，请查看上面的错误。
    pause
    exit /b 1
  )
)

start "" "%~dp0app\ButangPet.exe"
if errorlevel 1 (
  echo 启动失败，请查看 app\data\pet.log。
  pause
  exit /b 1
)
exit /b 0
