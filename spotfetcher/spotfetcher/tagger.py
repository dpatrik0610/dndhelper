"""Write ID3 tags (v2.3 for best compatibility with Windows Explorer, phones and car stereos)."""
from __future__ import annotations

import re
from pathlib import Path

from mutagen.id3 import (
    APIC, COMM, ID3, SYLT, TALB, TBPM, TCOM, TCON, TCOP, TDRC, TENC, TEXT, TIT2, TLEN, TPE1, TPE2,
    TPOS, TPUB, TRCK, TSRC, TSSE, TXXX, USLT, WOAS, Encoding,
)

from .models import Track
from .utils import session

_LRC_LINE = re.compile(r"\[(\d+):(\d+(?:\.\d+)?)\](.*)")


def _parse_lrc(lrc: str) -> list[tuple[str, int]]:
    out = []
    for line in lrc.splitlines():
        m = _LRC_LINE.match(line.strip())
        if m:
            ms = int((int(m.group(1)) * 60 + float(m.group(2))) * 1000)
            out.append((m.group(3).strip(), ms))
    return out


def fetch_cover(url: str | None) -> tuple[bytes, str] | None:
    if not url:
        return None
    candidates = [url]
    if "ab67616d000082c1" in url:  # Spotify's largest size isn't available for every release
        candidates.append(url.replace("ab67616d000082c1", "ab67616d0000b273"))
    for u in candidates:
        try:
            r = session.get(u, timeout=30)
            r.raise_for_status()
            return r.content, r.headers.get("Content-Type", "image/jpeg").split(";")[0]
        except Exception:
            continue
    return None


def _num(n: int | None, total: int | None) -> str | None:
    if not n:
        return None
    return f"{n}/{total}" if total else str(n)


def tag(path: Path, t: Track, bitrate: int, source_url: str | None = None) -> None:
    tags = ID3()
    enc = Encoding.UTF16  # v2.3 has no UTF-8; UTF-16 keeps non-Latin characters intact

    def text(frame, value):
        if value:
            tags.add(frame(encoding=enc, text=value if isinstance(value, list) else [str(value)]))

    def txxx(desc, value):
        if value:
            tags.add(TXXX(encoding=enc, desc=desc, text=value if isinstance(value, list) else [str(value)]))

    text(TIT2, t.title)
    text(TPE1, t.artists)
    text(TPE2, t.album_artists or t.artists[:1])
    text(TALB, t.album)
    text(TRCK, _num(t.track_number, t.track_total))
    text(TPOS, _num(t.disc_number, t.disc_total))
    text(TDRC, t.release_date)
    text(TCON, t.genres)
    text(TPUB, t.label)
    text(TCOP, t.copyright)
    text(TSRC, t.isrc)
    text(TBPM, round(t.bpm) if t.bpm else None)
    text(TCOM, t.composers)
    text(TEXT, t.lyricists)
    text(TLEN, t.duration_ms or None)
    text(TENC, "SpotFetcher")
    text(TSSE, f"LAME MP3 CBR {bitrate} kbps")

    txxx("ARTISTS", t.artists)
    txxx("BARCODE", t.upc)
    txxx("RELEASETYPE", t.album_type)
    txxx("ITUNESADVISORY", "1" if t.explicit else "0")
    txxx("SPOTIFY_TRACK_ID", t.spotify_id)
    txxx("DEEZER_TRACK_ID", t.deezer_id)
    txxx("YOUTUBE_VIDEO_ID", t.youtube_id)
    txxx("MusicBrainz Track Id", t.musicbrainz_recording_id)
    txxx("MusicBrainz Album Id", t.musicbrainz_release_id)
    txxx("MusicBrainz Artist Id", t.musicbrainz_artist_ids)
    txxx("SOURCE", source_url)
    tags.add(WOAS(url=t.spotify_url))
    tags.add(COMM(encoding=enc, lang="eng", desc="", text=[t.spotify_url]))

    if t.lyrics or t.synced_lyrics:
        plain = t.lyrics or "\n".join(line for line, _ in _parse_lrc(t.synced_lyrics or ""))
        tags.add(USLT(encoding=enc, lang="eng", desc="", text=plain))
    if t.synced_lyrics:
        tags.add(SYLT(encoding=enc, lang="eng", format=2, type=1, desc="", text=_parse_lrc(t.synced_lyrics)))

    cover = fetch_cover(t.cover_url)
    if cover:
        data, mime = cover
        tags.add(APIC(encoding=Encoding.LATIN1, mime=mime, type=3, desc="Cover", data=data))

    tags.save(path, v2_version=3, v23_sep="/")
