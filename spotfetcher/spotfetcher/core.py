"""The full pipeline: Spotify link -> metadata -> YouTube Music match -> tagged MP3."""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from . import deezer, musicbrainz, spotify, tagger, ytmusic
from .download import BITRATES, download_mp3
from .models import Track
from .utils import safe_filename

Log = Callable[[str], None]
# emit(track_id, stage, data): structured progress for the app UI.
Emit = Callable[[str, str, dict[str, Any]], None]


def _no_emit(_track_id: str, _stage: str, _data: dict[str, Any]) -> None:
    pass


@dataclass
class Options:
    output_dir: Path = Path("downloads")
    bitrate: int = 320
    cookies: str | None = None  # Netscape cookies.txt for YouTube Music Premium (256 kbps AAC source)
    musicbrainz: bool = True
    overwrite: bool = False
    max_bytes: int | None = None  # the MP3 must fit this: the quality drops if it would not


def fit_bitrate(bitrate: int, duration_s: float, max_bytes: int | None) -> int:
    """The chosen bitrate, or the highest lower one that fits. A margin covers a match a little longer than Spotify's."""
    if not max_bytes or not duration_s:
        return bitrate
    for kbps in sorted(BITRATES, reverse=True):
        if kbps <= bitrate and kbps * 1000 / 8 * duration_s * 1.05 <= max_bytes:
            return kbps
    raise ValueError(f"This song is too long to fit the {max_bytes // (1024 * 1024)} MB limit, even at the lowest quality.")


@dataclass
class Result:
    track: Track
    path: Path | None = None
    status: str = "ok"  # ok | skipped | not_found | error | cancelled
    message: str = ""


def thumbnail(url: str | None) -> str | None:
    """A small cover variant for displaying in the UI."""
    if url and "ab67616d000082c1" in url:
        return url.replace("ab67616d000082c1", "ab67616d00001e02")  # 300px
    if url and "1000x1000" in url:
        return url.replace("1000x1000", "250x250")
    return url


def track_info(t: Track) -> dict[str, Any]:
    return {
        "title": t.title,
        "artists": t.artists,
        "album": t.album,
        "duration": round(t.duration_s),
        "explicit": t.explicit,
        "cover": thumbnail(t.cover_url),
    }


def gather_metadata(track_id: str, opts: Options, log: Log, emit: Emit = _no_emit) -> Track:
    t = spotify.get_track(track_id)
    log(f"  Spotify:   {', '.join(t.artists)} - {t.title}" + (f"  [{t.album}]" if t.album else ""))
    emit(track_id, "metadata", track_info(t))
    if deezer.enrich(t):
        log(f"  Deezer:    ISRC {t.isrc or '?'}, label {t.label or '?'}, {t.bpm or '?'} BPM")
    if opts.musicbrainz and musicbrainz.enrich(t) and (t.composers or t.lyricists):
        log(f"  MusicBrainz: written by {', '.join(dict.fromkeys(t.composers + t.lyricists))}")
    emit(track_id, "metadata", track_info(t))  # album/cover may have been filled in
    return t


def build_path(t: Track, opts: Options, folder: str | None) -> Path:
    name = safe_filename(f"{', '.join(t.artists)} - {t.title}")
    base = opts.output_dir / safe_filename(folder) if folder else opts.output_dir
    return base / f"{name}.mp3"


def process_track(
    track_id: str, opts: Options, log: Log, folder: str | None = None, emit: Emit = _no_emit,
    pick: ytmusic.Match | None = None,  # the user's own choice; skips the automatic search
) -> Result:
    t = gather_metadata(track_id, opts, log, emit)
    dest = build_path(t, opts, folder)
    if dest.exists() and not opts.overwrite:
        log(f"  Skipped:   already exists ({dest.name})")
        emit(track_id, "skipped", {"path": str(dest), "message": "Already in your library"})
        return Result(t, dest, "skipped")

    emit(track_id, "matching", {})
    match = pick or ytmusic.find(t)
    if not match:
        log("  NOT FOUND on YouTube Music")
        emit(track_id, "not_found", {"message": "No confident match on YouTube Music"})
        return Result(t, None, "not_found", "no confident match on YouTube Music")
    t.youtube_id = match.video_id
    verified = match.score >= 1.0
    how = "picked by hand" if pick else "ISRC-verified" if verified else f"score {match.score:.2f}"
    log(f"  Match:     {', '.join(match.artists)} - {match.title} ({match.kind}, {how})")
    match_info = {"title": match.title, "artists": match.artists, "kind": match.kind, "verified": verified,
                  "score": round(match.score, 2), "url": match.url, "picked": bool(pick)}

    emit(track_id, "downloading", {"progress": 0, "match": match_info})

    def on_progress(stage: str, fraction: float | None) -> None:
        emit(track_id, stage, {"progress": fraction})

    # A hand-picked result can run far longer than Spotify's version.
    bitrate = fit_bitrate(opts.bitrate, max(t.duration_s, match.duration_s or 0), opts.max_bytes)
    src = download_mp3(match.video_id, dest, bitrate, opts.cookies, on_progress)
    log(f"  Source:    {src.get('codec')} ~{round(src.get('abr') or 0)} kbps -> MP3 {bitrate} kbps")
    emit(track_id, "tagging", {})
    tagger.tag(dest, t, bitrate, source_url=match.url)
    log(f"  Saved:     {dest}")
    emit(track_id, "done", {
        "path": str(dest),
        "match": match_info,
        "source": {"codec": src.get("codec"), "kbps": round(src.get("abr") or 0)},
        "bitrate": bitrate,
    })
    return Result(t, dest)

