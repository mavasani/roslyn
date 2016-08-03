@echo off

if exist "%VSINSTALLDIR%" goto done

:: prefer building with Dev15

set CommonToolsDir=%VS150COMNTOOLS%
if not exist "%CommonToolsDir%" set CommonToolsDir=%VS140COMNTOOLS%
if not exist "%CommonToolsDir%" exit /b 1

pushd
call "%CommonToolsDir%\VsDevCmd.bat"
popd

:done
exit /b 0