"""Resolve Spotify links to Track objects.

Uses the official Web API when SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET are set
(gives album, track/disc numbers, ISRC, label, copyright). Otherwise falls back
to the public embed page, and Deezer/MusicBrainz fill in the rest.
"""
from __future__ import annotations

import html
import json
import os
import re
import time

from .models import Track
from .utils import session

URL_RE = re.compile(
    r"(?:open\.spotify\.com/(?:intl-[\w-]+/)?(?:embed/)?|spotify:)(track|album|playlist)[/:]([A-Za-z0-9]{22})"
)
_NEXT_DATA = re.compile(r'<script id="__NEXT_DATA__" type="application/json">(.*?)</script>', re.S)


class SpotifyError(Exception):
    pass


def parse_url(url: str) -> tuple[str, str]:
    m = URL_RE.search(url.strip())
    if not m:
        raise SpotifyError(f"Not a Spotify track/album/playlist link: {url}")
    return m.group(1), m.group(2)


# Track titles/artists seen in album or playlist listings, keyed by track id.
previews: dict[str, dict] = {}

# ---------------------------------------------------------------- Web API

_token: tuple[str, float] | None = None


def _api_enabled() -> bool:
    return bool(os.getenv("SPOTIFY_CLIENT_ID") and os.getenv("SPOTIFY_CLIENT_SECRET"))


def _api_get(path: str, **params) -> dict:
    global _token
    if not _token or _token[1] < time.time():
        r = session.post(
            "https://accounts.spotify.com/api/token",
            data={"grant_type": "client_credentials"},
            auth=(os.environ["SPOTIFY_CLIENT_ID"], os.environ["SPOTIFY_CLIENT_SECRET"]),
            timeout=15,
        )
        r.raise_for_status()
        j = r.json()
        _token = (j["access_token"], time.time() + j["expires_in"] - 60)
    r = session.get(
        f"https://api.spotify.com/v1{path}",
        params=params,
        headers={"Authorization": f"Bearer {_token[0]}"},
        timeout=15,
    )
    r.raise_for_status()
    return r.json()


_album_cache: dict[str, dict] = {}


def _api_album(album_id: str) -> dict:
    if album_id not in _album_cache:
        _album_cache[album_id] = _api_get(f"/albums/{album_id}")
    return _album_cache[album_id]


def _api_track(track_id: str) -> Track:
    t = _api_get(f"/tracks/{track_id}")
    album = t.get("album") or {}
    try:
        album = _api_album(album["id"]) | album
    except Exception:
        pass
    tracks = (album.get("tracks") or {}).get("items") or []
    copyrights = album.get("copyrights") or []
    copyright_text = next((c["text"] for c in copyrights if c.get("type") == "P"), None) or next(
        (c["text"] for c in copyrights), None
    )
    images = sorted(album.get("images") or [], key=lambda i: i.get("width") or 0, reverse=True)
    return Track(
        spotify_id=t["id"],
        title=t["name"],
        artists=[a["name"] for a in t.get("artists", [])],
        duration_ms=t.get("duration_ms") or 0,
        explicit=bool(t.get("explicit")),
        album=album.get("name"),
        album_artists=[a["name"] for a in album.get("artists", [])],
        album_type=album.get("album_type"),
        track_number=t.get("track_number"),
        track_total=album.get("total_tracks"),
        disc_number=t.get("disc_number"),
        disc_total=max((i.get("disc_number") or 1 for i in tracks), default=None) if tracks else None,
        release_date=album.get("release_date"),
        isrc=(t.get("external_ids") or {}).get("isrc"),
        upc=(album.get("external_ids") or {}).get("upc"),
        label=album.get("label"),
        copyright=copyright_text,
        genres=album.get("genres") or [],
        cover_url=_hires_cover(images[0]["url"]) if images else None,
    )


def _remember(t: dict) -> str:
    """Cache a track's title/artists from a collection listing for instant UI previews."""
    previews[t["id"]] = {"title": t.get("name"), "artists": [a["name"] for a in t.get("artists") or []]}
    return t["id"]


def _api_collection(kind: str, cid: str) -> tuple[str, list[str]]:
    if kind == "album":
        album = _api_album(cid)
        ids = [_remember(i) for i in album["tracks"]["items"]]
        page = album["tracks"]
        while page.get("next"):
            page = session.get(page["next"], headers={"Authorization": f"Bearer {_token[0]}"}, timeout=15).json()
            ids += [_remember(i) for i in page["items"]]
        return album["name"], ids
    pl = _api_get(f"/playlists/{cid}", fields="name")
    ids, offset = [], 0
    while True:
        page = _api_get(
            f"/playlists/{cid}/tracks", offset=offset, limit=100, fields="items(track(id,type,name,artists(name))),next"
        )
        ids += [
            _remember(i["track"]) for i in page["items"]
            if i.get("track") and i["track"].get("type") == "track" and i["track"].get("id")
        ]
        if not page.get("next"):
            break
        offset += 100
    return pl["name"], ids


# ---------------------------------------------------------------- Embed page fallback


def _embed(kind: str, eid: str) -> dict:
    r = session.get(
        f"https://open.spotify.com/embed/{kind}/{eid}",
        headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130 Safari/537.36"},
        timeout=15,
    )
    r.raise_for_status()
    m = _NEXT_DATA.search(r.text)
    if not m:
        raise SpotifyError(f"Could not read Spotify page for {kind} {eid}")
    try:
        return json.loads(m.group(1))["props"]["pageProps"]["state"]["data"]["entity"]
    except (KeyError, TypeError) as e:
        raise SpotifyError(f"Spotify {kind} {eid} not found or unavailable") from e


def _hires_cover(url: str) -> str:
    # Spotify cover URLs encode the size in the hash prefix; 82c1 is the largest variant.
    # The tagger falls back to the 640px version if it doesn't exist.
    for small in ("ab67616d0000b273", "ab67616d00001e02", "ab67616d00004851"):
        if small in url:
            return url.replace(small, "ab67616d000082c1")
    return url


_embed_album_cache: dict[str, dict] = {}
_META = re.compile(r'<meta (?:name|property)="([^"]+)" content="([^"]*)"')


def _page_meta(kind: str, eid: str) -> list[tuple[str, str]]:
    """Server-rendered <meta> tags (Spotify only renders them for link-preview bots)."""
    try:
        r = session.get(
            f"https://open.spotify.com/{kind}/{eid}", headers={"User-Agent": "facebookexternalhit/1.1"}, timeout=15
        )
        return [(k, html.unescape(v)) for k, v in _META.findall(r.text)]
    except Exception:
        return []


def _embed_album(album_id: str) -> dict:
    """Album name/artist plus {track_id: (disc, track)} positions, cached per album."""
    if album_id in _embed_album_cache:
        return _embed_album_cache[album_id]
    info: dict = {"positions": {}}
    try:
        e = _embed("album", album_id)
        info["name"] = e.get("title") or e.get("name")
        info["artist"] = e.get("subtitle")
    except Exception:
        pass
    song = disc = None
    for key, value in _page_meta("album", album_id):
        if key == "music:song":
            song = value.rsplit("/", 1)[-1]
        elif key == "music:song:disc":
            disc = int(value)
        elif key == "music:song:track" and song:
            info["positions"][song] = (disc or 1, int(value))
        elif key == "music:release_date":
            info["release_date"] = value
    _embed_album_cache[album_id] = info
    return info


def _embed_track(track_id: str) -> Track:
    e = _embed("track", track_id)
    images = sorted((e.get("visualIdentity") or {}).get("image") or [], key=lambda i: i.get("maxWidth") or 0, reverse=True)
    t = Track(
        spotify_id=track_id,
        title=e.get("title") or e["name"],
        artists=[a["name"] for a in e.get("artists") or []],
        duration_ms=e.get("duration") or 0,
        explicit=bool(e.get("isExplicit")),
        release_date=((e.get("releaseDate") or {}).get("isoString") or "")[:10] or None,
        cover_url=_hires_cover(images[0]["url"]) if images else None,
    )
    meta = dict(_page_meta("track", track_id))
    album_id = meta.get("music:album", "").rsplit("/", 1)[-1]
    if album_id:
        album = _embed_album(album_id)
        positions = album["positions"]
        disc, num = positions.get(track_id, (None, None))
        t.album = album.get("name")
        t.album_artists = [album["artist"]] if album.get("artist") else []
        t.disc_number = disc
        t.track_number = num or (int(meta["music:album:track"]) if meta.get("music:album:track") else None)
        if positions:
            t.track_total = len(positions)
            t.disc_total = max(d for d, _ in positions.values())
        t.release_date = album.get("release_date") or meta.get("music:release_date") or t.release_date
    return t


def _embed_collection(kind: str, cid: str) -> tuple[str, list[str]]:
    e = _embed(kind, cid)
    ids = []
    for t in e.get("trackList") or []:
        if t.get("uri", "").startswith("spotify:track:"):
            tid = t["uri"].rsplit(":", 1)[-1]
            # The embed lists artists as one joined "subtitle" string.
            previews[tid] = {"title": t.get("title"), "artists": [t["subtitle"]] if t.get("subtitle") else []}
            ids.append(tid)
    return e.get("title") or e.get("name") or cid, ids


# ---------------------------------------------------------------- Public API


def get_track(track_id: str) -> Track:
    if _api_enabled():
        try:
            return _api_track(track_id)
        except Exception:
            pass  # fall back to the embed page
    return _embed_track(track_id)


def resolve(url: str) -> tuple[str, str | None, list[str]]:
    """Return (kind, collection name, track ids) for a link."""
    kind, sid = parse_url(url)
    if kind == "track":
        return kind, None, [sid]
    if _api_enabled():
        try:
            name, ids = _api_collection(kind, sid)
            return kind, name, ids
        except Exception:
            pass
    name, ids = _embed_collection(kind, sid)
    return kind, name, ids
