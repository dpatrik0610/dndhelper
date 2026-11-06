# SpotFetcher

Built by [Shiro](https://github.com/BMNHunter), who also made Bardify.

The bard pastes a Spotify link on the Bardify page and presses Fetch. For each song the API asks this helper to:

1. read the song's details from Spotify,
2. find the same recording on YouTube Music,
3. download it with yt-dlp and convert it to a tagged MP3 with ffmpeg.

The API then stores the MP3 and its cover in R2, the same way as an upload, and puts the song in the campaign's Unsorted collection. Progress streams back to the page as it goes. Nothing passes through the bard's browser.

## Where it runs

The API starts the helper itself (`Services/SpotFetcherHost.cs`), in Docker and with `dotnet run` alike. On first start it creates a Python environment and installs `requirements.txt`. On every start it updates the packages, so yt-dlp keeps up with YouTube. If the helper stops, it restarts within a few seconds. If something else already listens on the helper's port, the API uses that one.

The helper listens on `127.0.0.1:47821`, so only the API on the same machine can reach it. It refuses any request that carries a browser `Origin` or a `Host` other than localhost.

The machine needs Python 3.10+ and Deno or Node.js. ffmpeg comes from the `imageio-ffmpeg` package unless one is already on PATH.

| Setting | |
|---|---|
| `SpotFetcher__Url` (API) | Where the API finds the helper. Default: `http://127.0.0.1:47821`. |
| `SpotFetcher__Dir` (API) | This folder. Default: `spotfetcher` next to the API. |
| `SpotFetcher__Venv` (API) | The helper's Python environment. Default: `%LOCALAPPDATA%\SpotFetcher\venv` (in Docker: `/opt/spotfetcher/venv`). |
| `SpotFetcher__Python` (API) | The Python used to create it. Default: `python` on Windows, `python3` elsewhere. |
| `SPOTFETCHER_COOKIES` (helper) | Optional path to a YouTube Music `cookies.txt`. Servers often need this; see below. Treat the file like a password. |
| `SPOTIFY_CLIENT_ID`, `SPOTIFY_CLIENT_SECRET` (helper) | Optional, for fuller song details from Spotify's API. |

**On a server:** YouTube often blocks downloads from datacenter IP addresses ("Sign in to confirm you're not a bot"). If fetches fail that way, export a `cookies.txt` from a signed-in YouTube account, put it in the container, and set `SPOTFETCHER_COOKIES` to its path.

**Before you use it:** downloading from YouTube Music goes against YouTube's terms of service, and most music is under copyright. Sharing the files with other people, even a small private group, may need permission depending on where you live. Whoever runs the server is responsible for that, so check before you use it.

## Tests

```
cd dndhelper/spotfetcher && python -m unittest test_helper
```
