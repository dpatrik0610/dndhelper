@echo off
setlocal
set "VENV=%LOCALAPPDATA%\SpotFetcher\venv"
cd /d "%~dp0"
if not exist "%VENV%\Scripts\python.exe" (
    echo Setting up SpotFetcher for the first time...
    python -m venv "%VENV%" || goto :error
)
"%VENV%\Scripts\python" -m pip install --quiet --disable-pip-version-check --upgrade -r requirements.txt
"%VENV%\Scripts\python" -c "import yt_dlp, ytmusicapi, mutagen, requests" 2>nul || goto :error
"%VENV%\Scripts\python" -m spotfetcher
exit /b 0

:error
echo Setup failed. Make sure Python 3.10+ is installed and on PATH.
pause
exit /b 1
