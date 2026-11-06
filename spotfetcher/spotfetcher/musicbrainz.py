"""Songwriter credits and MusicBrainz IDs, looked up by ISRC."""
from __future__ import annotations

import time

from .models import Track
from .utils import session, similarity

API = "https://musicbrainz.org/ws/2"
_last_call = 0.0


def _get(path: str, **params) -> dict | None:
    global _last_call
    wait = 1.1 - (time.time() - _last_call)  # MusicBrainz allows 1 request/second
    if wait > 0:
        time.sleep(wait)
    _last_call = time.time()
    try:
        r = session.get(f"{API}{path}", params={"fmt": "json", **params}, timeout=20)
        return r.json() if r.ok else None
    except Exception:
        return None


def enrich(track: Track) -> bool:
    if not track.isrc:
        return False
    res = _get(f"/isrc/{track.isrc}") or {}
    recordings = res.get("recordings") or []
    if not recordings:
        return False
    rec_id = max(recordings, key=lambda r: similarity(track.title, r.get("title", "")))["id"]
    rec = _get(f"/recording/{rec_id}", inc="artist-credits+releases+work-rels+artist-rels") or {}
    track.musicbrainz_recording_id = rec_id
    track.musicbrainz_artist_ids = [c["artist"]["id"] for c in rec.get("artist-credit", []) if c.get("artist")]

    releases = rec.get("releases") or []
    if releases and track.album:
        best = max(releases, key=lambda r: similarity(track.album, r.get("title", "")))
        if similarity(track.album, best.get("title", "")) >= 0.85:
            track.musicbrainz_release_id = best["id"]

    composers, lyricists = [], []
    works = [r["work"] for r in rec.get("relations", []) if r.get("type") == "performance" and r.get("work")]
    for work in works[:3]:
        w = _get(f"/work/{work['id']}", inc="artist-rels") or {}
        for rel in w.get("relations", []):
            name = (rel.get("artist") or {}).get("name")
            if not name:
                continue
            if rel.get("type") in ("composer", "writer") and name not in composers:
                composers.append(name)
            if rel.get("type") in ("lyricist", "writer") and name not in lyricists:
                lyricists.append(name)
    track.fill(composers=composers, lyricists=lyricists)
    return True
