@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "BUILD_FILE=%SCRIPT_DIR%build.cs"

pushd "%SCRIPT_DIR%" || exit /b 1
dotnet run --file "%BUILD_FILE%" -- %*
set "EXIT_CODE=%ERRORLEVEL%"
popd

exit /b %EXIT_CODE%