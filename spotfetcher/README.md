# SpotFetcher

Built by [Shiro](https://github.com/BMNHunter), who also made Bardify.

The bard pastes a Spotify link on the Bardify page and presses Fetch. For each song, the page asks this helper, running on the bard's own computer, to:

1. read the song's details from Spotify,
2. find the same recording on YouTube Music,
3. download it with yt-dlp and convert it to a tagged MP3 with ffmpeg.

The page then uploads the MP3 like any other song, with the bard's own sign-in, into the campaign's Unsorted collection. The helper holds no site credentials.

## Why on the bard's computer

YouTube blocks downloads from datacenter IP addresses ("Sign in to confirm you're not a bot"), so the helper can't run next to the API. On a home connection it works without any account.

## For bards

Download `SpotFetcher-windows.zip` from the [latest release](https://github.com/dpatrik0610/dndhelper/releases/latest), unzip it, and open `SpotFetcher.exe`. Keep its window open while fetching. Chrome and Edge may ask once to let the site reach devices on your network: allow it. Safari can't reach a local helper, so use another browser.

`SpotFetcher.exe` carries Python, yt-dlp and ffmpeg. `deno.exe` beside it is the JavaScript runtime yt-dlp needs for YouTube. Keep the two together.

## Security

The helper listens on `127.0.0.1:47821` only. It answers only pages from `SPOTFETCHER_ORIGINS`, and refuses any `Host` other than this computer (DNS rebinding).

| Setting | |
|---|---|
| `SPOTFETCHER_ORIGINS` | Sites allowed to use the helper, comma-separated. Default: `https://dndreforged.quest`, `https://www.dndreforged.quest`, `http://localhost:3000`. |
| `SPOTFETCHER_COOKIES` | Optional path to a YouTube Music `cookies.txt`. Treat the file like a password. |
| `SPOTIFY_CLIENT_ID`, `SPOTIFY_CLIENT_SECRET` | Optional, for fuller song details from Spotify's API. |

**Before you use it:** downloading from YouTube Music goes against YouTube's terms of service, and most music is under copyright. Sharing the files with other people, even a small private group, may need permission depending on where you live. Each bard is responsible for what they download, so check before you use it.

## Development

Run from source with `start-helper.bat` (needs Python 3.10+, and Deno or Node.js). It sets up `%LOCALAPPDATA%\SpotFetcher\venv` and updates yt-dlp on every start.

Tests: `python -m unittest test_helper`

## Releasing the .exe

yt-dlp has to keep up with YouTube, so rebuild and release when fetches start failing, or every few weeks.

- **On GitHub:** run the *SpotFetcher release* workflow (Actions tab), or push a tag like `spotfetcher-v0.3.0`. It builds the zip and publishes it as the latest release, which the Bardify page links to.
- **Locally:** `powershell -ExecutionPolicy Bypass -File build-exe.ps1` writes `dist\SpotFetcher-windows.zip`.
