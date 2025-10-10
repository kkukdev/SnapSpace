@echo off
cls

echo.===================================================
echo. Initializing Git Commit Environment Setup
echo.===================================================
echo.

echo.[1/2] Setting up project commit template...
git config commit.template .gitlab/.gitmessage.txt
echo.[SUCCESS] Commit template has been set to '.gitlab/.gitmessage.txt'.
echo.
echo.---------------------------------------------------
echo.

echo.[2/2] Configuring the default Git editor.
echo.This specifies the program that opens when you run 'git commit'.
echo.
set /p setEditor="Would you like to set VS Code as your default editor? (y/n): "

if /i "%setEditor%"=="y" (
    echo.
    git config --global core.editor "code --wait"
    echo.[SUCCESS] VS Code will now open when you run 'git commit'.
) else (
    echo.
    echo.-> Skipping editor configuration.
)

echo.
echo.===================================================
echo. All configurations are complete.
echo.===================================================
echo.
pause