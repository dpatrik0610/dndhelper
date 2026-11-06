$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$venv = "build\venv"
if (-not (Test-Path "$venv\Scripts\python.exe")) { python -m venv $venv }
$py = "$venv\Scripts\python.exe"
& $py -m pip install --quiet --disable-pip-version-check --upgrade -r requirements.txt pyinstaller
if ($LASTEXITCODE) { throw "pip install failed" }

& $py -m PyInstaller launch.py --onefile --console --noconfirm --name SpotFetcher `
    --distpath build\exe --workpath build\work --specpath build `
    --collect-all yt_dlp_ejs --collect-all imageio_ffmpeg --collect-data ytmusicapi
if ($LASTEXITCODE) { throw "PyInstaller failed" }

$deno = "build\deno.zip"
if (-not (Test-Path $deno)) {
    Invoke-WebRequest "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip" -OutFile $deno
}
Expand-Archive $deno -DestinationPath build\exe -Force

New-Item -ItemType Directory -Force dist | Out-Null
Compress-Archive build\exe\SpotFetcher.exe, build\exe\deno.exe -DestinationPath dist\SpotFetcher-windows.zip -Force
Write-Host "Built dist\SpotFetcher-windows.zip"
