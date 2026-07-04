@echo off
setlocal

set "REPO_ROOT=%~dp0.."
for %%I in ("%REPO_ROOT%") do set "REPO_ROOT=%%~fI"

if not defined FLUTTER_BAT set "FLUTTER_BAT=E:\flutter\bin\flutter.bat"

if not exist "%FLUTTER_BAT%" (
    echo error: "%FLUTTER_BAT%" was not found. Set FLUTTER_BAT to an explicit flutter.bat path.
    exit /b 1
)

for %%I in ("%FLUTTER_BAT%") do set "FLUTTER_BIN=%%~dpI"
for %%I in ("%FLUTTER_BIN%..") do set "FLUTTER_ROOT=%%~fI"

set "FLUTTER_SUPPRESS_ANALYTICS=true"
set "FLUTTER_CACHE=%FLUTTER_ROOT%\bin\cache"
set "FLUTTER_CACHE_PROBE=%FLUTTER_CACHE%\verify-fast-%RANDOM%-%RANDOM%.tmp"
del "%FLUTTER_CACHE_PROBE%" >nul 2>nul
cmd /d /c "echo verify-fast>"%FLUTTER_CACHE_PROBE%"" >nul 2>nul
if not exist "%FLUTTER_CACHE_PROBE%" (
    echo error: Flutter cache is not writable at "%FLUTTER_CACHE%".
    echo        Run from an unrestricted terminal or allow the Flutter command outside the sandbox.
    exit /b 1
)
del "%FLUTTER_CACHE_PROBE%" >nul 2>nul

pushd "%REPO_ROOT%"
if errorlevel 1 exit /b 1

echo.
echo ==^> flutter analyze targeted shell/client files
call "%FLUTTER_BAT%" analyze ^
    app\lib\grpc\action_dispatch.dart ^
    app\lib\shell\forui_app_shell.dart ^
    app\lib\features\experience\experience_host_screen.dart ^
    app\lib\features\canvas\living_canvas_screen.dart ^
    app\test\grpc\action_dispatch_test.dart ^
    app\test\shell\forui_app_shell_test.dart
set "ExitCode=%ERRORLEVEL%"
if not "%ExitCode%"=="0" (
    echo flutter analyze targeted shell/client files failed with exit code %ExitCode%.
    popd
    exit /b %ExitCode%
)
echo ok: flutter analyze targeted shell/client files

pushd "%REPO_ROOT%\app"
if errorlevel 1 (
    popd
    exit /b 1
)

echo.
echo ==^> flutter test targeted shell/client tests
call "%FLUTTER_BAT%" test ^
    test\grpc\action_dispatch_test.dart ^
    test\shell\forui_app_shell_test.dart
set "ExitCode=%ERRORLEVEL%"
popd
if not "%ExitCode%"=="0" (
    echo flutter test targeted shell/client tests failed with exit code %ExitCode%.
    popd
    exit /b %ExitCode%
)
echo ok: flutter test targeted shell/client tests

popd
exit /b 0
