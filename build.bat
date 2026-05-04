@echo off
set MSBUILD_EXE_PATH=C:\Program Files\dotnet\sdk\9.0.313\MSBuild.dll
cd /d "C:\Users\ASUS\战旗之王\goddot"
echo Building...
dotnet build
if %ERRORLEVEL% == 0 (
    echo Build successful!
    echo Running...
    "C:\Users\ASUS\Downloads\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64.exe" --path "." --headless
) else (
    echo Build failed!
)
pause
