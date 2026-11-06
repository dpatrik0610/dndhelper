"""Download the matched audio with yt-dlp and encode it to MP3 with ffmpeg."""
from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Callable

from yt_dlp import YoutubeDL

BITRATES = (320, 256, 192, 128)


class DownloadError(Exception):
    pass


# on_progress(stage, fraction): stage is "downloading" or "converting", fraction 0..1 or None.
Progress = Callable[[str, float | None], None]


def ffmpeg_path() -> str:
    path = shutil.which("ffmpeg")
    if path:
        return path
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception as e:
        raise DownloadError("ffmpeg was not found. Install it, or reinstall requirements.txt.") from e


def js_runtimes() -> dict:
    bundled = Path(sys.executable).parent / "deno.exe"
    if getattr(sys, "frozen", False) and bundled.exists():
        return {"deno": {"path": str(bundled)}}
    return {"deno": {}, "node": {}, "bun": {}}


def download_mp3(
    video_id: str, dest: Path, bitrate: int, cookies: str | None = None, on_progress: Progress | None = None
) -> dict:
    """Fetch the best audio stream for `video_id` and write a CBR MP3 to `dest`.

    Returns info about the source stream (codec, bitrate) for logging.
    """
    if bitrate not in BITRATES:
        raise ValueError(f"bitrate must be one of {BITRATES}")
    ffmpeg = ffmpeg_path()
    report = on_progress or (lambda _stage, _fraction: None)

    def hook(d: dict) -> None:
        if d.get("status") == "downloading":
            total = d.get("total_bytes") or d.get("total_bytes_estimate")
            report("downloading", d["downloaded_bytes"] / total if total else None)

    with tempfile.TemporaryDirectory(prefix="spotfetcher-") as tmp:
        opts = {
            "format": "bestaudio/best",
            "outtmpl": str(Path(tmp) / "%(id)s.%(ext)s"),
            "quiet": True,
            "no_warnings": True,
            "noprogress": True,
            "noplaylist": True,
            "ffmpeg_location": ffmpeg,
            # YouTube needs a JS runtime to unlock all formats (avoids 403s); use whichever is installed.
            "js_runtimes": js_runtimes(),
            "retries": 5,
            "fragment_retries": 5,
            "progress_hooks": [hook],
        }
        if cookies:
            opts["cookiefile"] = cookies
        try:
            with YoutubeDL(opts) as ydl:
                info = ydl.extract_info(f"https://music.youtube.com/watch?v={video_id}", download=True)
                src = Path(ydl.prepare_filename(info))
        except Exception as e:
            raise DownloadError(f"yt-dlp failed: {e}") from e
        if not src.exists():
            found = list(Path(tmp).glob(f"{video_id}.*"))
            if not found:
                raise DownloadError("yt-dlp finished but no audio file was written")
            src = found[0]

        dest.parent.mkdir(parents=True, exist_ok=True)
        part = dest.with_suffix(".part.mp3")
        cmd = [
            ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1",
            "-i", str(src),
            "-vn", "-map_metadata", "-1",
            "-c:a", "libmp3lame", "-b:a", f"{bitrate}k", "-ar", "44100",
            str(part),
        ]
        report("converting", 0)
        duration = info.get("duration") or 0
        proc = subprocess.Popen(
            cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        for line in proc.stdout:  # "key=value" lines from -progress
            if line.startswith("out_time_us=") and duration:
                try:
                    report("converting", min(1.0, int(line.split("=", 1)[1]) / 1e6 / duration))
                except ValueError:
                    pass
        stderr = proc.stderr.read()
        if proc.wait() != 0:
            part.unlink(missing_ok=True)
            raise DownloadError(f"ffmpeg failed: {stderr.strip()[-500:]}")
        part.replace(dest)
    return {"codec": info.get("acodec"), "abr": info.get("abr"), "format": info.get("format_id")}
