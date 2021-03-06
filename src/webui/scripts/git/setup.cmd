@ECHO off & setlocal enableextensions enabledelayedexpansion

:: strlen("\scripts\git\") => 13
SET APP_HOME=%~dp0
SET APP_HOME=%APP_HOME:~0,-13%

cd %APP_HOME%

:: Install git hook
    echo Adding pre-commit hook...
    mkdir .git\hooks\ > NUL 2>&1
    del /F .git\hooks\pre-commit > NUL 2>&1
    copy scripts\git\pre-commit-runner.sh .git\hooks\pre-commit
    IF %ERRORLEVEL% NEQ 0 GOTO FAIL
    echo Done.
    goto :GIT_SETUP

:: Git setup
    git config --local core.whitespace trailing-space,space-before-tab
    git config --local core.autocrlf false
    git config --local core.eol lf
    git config --local apply.whitespace fix

    git config --local alias.st status
    git config --local alias.co checkout
    git config --local alias.ci commit

    git config --local alias.branches "branch -v -a"
    git config --local alias.lg "log --graph --pretty=format:'%Cred%h%Creset -%C(yellow)%d%Creset %s %Cgreen(%cr) %C(bold blue)<%an>%Creset' --abbrev-commit --date=relative"
    git config --local alias.lgx "log --stat"
    git config --local alias.lgt "log --graph --pretty=oneline --oneline --all"
    git config --local alias.lg1 "log --reverse --pretty=oneline"
    git config --local alias.stashdiff "stash show --patience"

    goto :END


:USAGE
    echo ERROR: sandboxing mode not specified.
    echo.
    echo The pre-commit hook can run in two different modes:
    echo   Without sandbox: the build process runs using .NET Core and other dependencies from your workstation
    echo   With sandbox: the build process runs inside a Docker container so you don't need to install .NET Core and other dependencies
    echo.
    echo Usage:
    echo .\scripts\git\setup
    echo .\scripts\git\setup --no-sandbox
    echo .\scripts\git\setup --with-sandbox
    exit /B 1


:FAIL
    echo Command failed
    endlocal
    exit /B 1


:END
endlocal
