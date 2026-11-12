@echo off
setlocal
echo TATBIM Project Creator v0.2.10 - installer
tasklist /FI "IMAGENAME eq Revit.exe" | find /I "Revit.exe" >nul && (echo ERROR: Close Revit.exe before installing & pause & exit /b 1)
set "RevitPath=C:\Program Files\Autodesk\Revit 2022"
if not exist "%RevitPath%\RevitAPI.dll" (echo Revit 2022 API not found & pause & exit /b 1)
dotnet build "%~dp0TATBIM.ProjectCreator.csproj" -c Release -p:RevitPath="%RevitPath%" || (pause & exit /b 1)
set "DST=%APPDATA%\Autodesk\Revit\Addins\2022\TATBIM.ProjectCreator"
if not exist "%DST%" mkdir "%DST%"
copy /y "%~dp0bin\Release\net48\TATBIM.ProjectCreator.dll" "%DST%\" >nul || (echo ERROR: DLL copy failed & pause & exit /b 1)
powershell -NoProfile -Command "$src='%~dp0bin\Release\net48\TATBIM.ProjectCreator.dll'; $dst=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022\TATBIM.ProjectCreator\TATBIM.ProjectCreator.dll'; if ((Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash) { Write-Host 'ERROR: Installed DLL differs from built DLL'; exit 1 } else { Write-Host 'DLL hash verified' }" || (pause & exit /b 1)
if not exist "%DST%\settings.json" copy /y "%~dp0settings.json" "%DST%\settings.json" >nul
if not exist "%DST%\Worksets" mkdir "%DST%\Worksets"
copy /y "%~dp0tatbim_logo.png" "%DST%\" >nul
xcopy /y /q "%~dp0Data\Worksets\*.xlsx" "%DST%\Worksets\" >nul
powershell -NoProfile -Command "$p=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022\TATBIM.ProjectCreator\settings.json'; $j=Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json; if (-not $j.WorksetsRoot) { $j.WorksetsRoot=(Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022\TATBIM.ProjectCreator\Worksets'); $j | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $p -Encoding UTF8 }"
powershell -NoProfile -Command "$a=Get-Content -LiteralPath '%~dp0TATBIM.ProjectCreator.addin' -Raw; $a=$a.Replace('TATBIM.ProjectCreator\TATBIM.ProjectCreator.dll', [System.Security.SecurityElement]::Escape((Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022\TATBIM.ProjectCreator\TATBIM.ProjectCreator.dll'))); [System.IO.File]::WriteAllText((Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022\TATBIM.ProjectCreator.addin'), $a, (New-Object System.Text.UTF8Encoding($false)))"
if not exist "%APPDATA%\Autodesk\Revit\Addins\2022\TATBIM.ProjectCreator.addin" (echo ERROR: .addin manifest not installed & pause & exit /b 1)
if not exist "%DST%\TATBIM.ProjectCreator.dll" (echo ERROR: DLL not installed & pause & exit /b 1)
echo Installation completed: TATBIM Project Creator v0.2.10. Restart Revit 2022.
pause
