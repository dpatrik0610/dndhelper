"""Metadata enrichment from Deezer's free public API (metadata only, no audio).

Album-level fields (UPC, label, genres...) are only copied when the Deezer album
is the same release Spotify shows, so a track never gets a compilation's tags.
"""
from __future__ import annotations

from .models import Track
from .utils import artist_overlap, clean_title, duration_score, session, similarity

API = "https://api.deezer.com"


def _get(path: str, **params) -> dict | None:
    try:
        r = session.get(f"{API}{path}", params=params, headers={"Accept-Language": "en"}, timeout=15)
        j = r.json()
    except Exception:
        return None
    return None if not isinstance(j, dict) or "error" in j else j


def _track_score(track: Track, cand: dict) -> float:
    artists = [(cand.get("artist") or {}).get("name", "")] + [c.get("name", "") for c in cand.get("contributors", [])]
    return (
        0.45 * similarity(track.title, cand.get("title", ""))
        + 0.35 * artist_overlap(track.artists, artists)
        + 0.20 * duration_score(track.duration_s, cand.get("duration"), tolerance=10)
    )


def _same_album(track: Track, album: dict | None) -> bool:
    if not album or not track.album:
        return False
    if similarity(track.album, album.get("title", "")) < 0.85:
        return False
    return not track.track_total or not album.get("nb_tracks") or track.track_total == album["nb_tracks"]


def _find_album(track: Track) -> dict | None:
    """Locate the exact album Spotify shows, then the track inside it."""
    if not track.album:
        return None
    artist = (track.album_artists or track.artists or [""])[0]
    seen = set()
    cands = []
    for q in (f'artist:"{artist}" album:"{track.album}"', f"{artist} {clean_title(track.album)}"):
        cands += [c for c in (_get("/search/album", q=q, limit=10) or {}).get("data", []) if c["id"] not in seen]
        seen.update(c["id"] for c in cands)
    for cand in cands:
        if similarity(track.album, cand.get("title", "")) < 0.85:
            continue
        album = _get(f"/album/{cand['id']}")
        if not _same_album(track, album):
            continue
        tracks = (album.get("tracks") or {}).get("data") or []
        best = max(tracks, key=lambda t: _track_score(track, t), default=None)
        if best and _track_score(track, best) >= 0.7:
            full = _get(f"/track/{best['id']}")
            if full:
                return {"track": full, "album": album}
    return None


def _find_any(track: Track) -> dict | None:
    """Fallback: same recording on any release (ISRC first, then search)."""
    if track.isrc:
        hit = _get(f"/track/isrc:{track.isrc}")
        if hit and hit.get("id"):
            return hit
    best, best_score = None, 0.0
    queries = dict.fromkeys([
        f'artist:"{track.main_artist}" track:"{track.title}"',
        f"{track.main_artist} {track.title}",
        f"{track.main_artist} {clean_title(track.title)}",
    ])
    for q in queries:
        for cand in (_get("/search", q=q, limit=15) or {}).get("data", []):
            s = _track_score(track, cand)
            if s > best_score:
                best, best_score = cand, s
        if best_score > 0.85:
            break
    if not best or best_score < 0.7:
        return None
    return _get(f"/track/{best['id']}")


def enrich(track: Track) -> bool:
    hit = _find_album(track)
    if hit:
        d, album = hit["track"], hit["album"]
    else:
        d = _find_any(track)
        if not d:
            return False
        album = _get(f"/album/{d['album']['id']}") if d.get("album") else None
        if not _same_album(track, album) and track.album:
            album = None  # different release: keep only recording-level data

    track.deezer_id = d.get("id")
    # Recording-level data is the same on every release of the recording.
    track.fill(isrc=d.get("isrc"), bpm=d.get("bpm") or None)
    if album:
        track.fill(
            album=album.get("title"),
            album_artists=[album["artist"]["name"]] if album.get("artist") else [],
            album_type=album.get("record_type"),
            track_number=d.get("track_position"),
            track_total=album.get("nb_tracks"),
            disc_number=d.get("disk_number"),
            release_date=album.get("release_date"),
            upc=album.get("upc"),
            label=album.get("label"),
            genres=[g["name"] for g in (album.get("genres") or {}).get("data", [])],
            cover_url=album.get("cover_xl"),
        )
    return True
