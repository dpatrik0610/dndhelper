"""Find the matching recording on YouTube Music."""
from __future__ import annotations

from dataclasses import dataclass

from ytmusicapi import YTMusic

from .models import Track
from .utils import artist_overlap, duration_score, normalize, similarity

_yt: YTMusic | None = None


def _client() -> YTMusic:
    global _yt
    if _yt is None:
        _yt = YTMusic()
    return _yt


@dataclass
class Match:
    video_id: str
    title: str
    artists: list[str]
    duration_s: int | None
    kind: str  # "song" (official audio) or "video"
    score: float

    @property
    def url(self) -> str:
        return f"https://music.youtube.com/watch?v={self.video_id}"


def _match(r: dict, score: float, kind: str | None = None) -> Match:
    return Match(
        video_id=r["videoId"],
        title=r.get("title") or "",
        artists=[a["name"] for a in r.get("artists") or [] if a.get("name")],
        duration_s=r.get("duration_seconds"),
        kind=r.get("resultType") or kind or "song",
        score=score,
    )


def _score(track: Track, r: dict, from_isrc: bool = False) -> float:
    artists = [a["name"] for a in r.get("artists") or [] if a.get("name")]
    title = r.get("title") or ""
    title_sim = similarity(track.title, title)
    free_text = ""
    if r.get("resultType") == "video":
        # Music videos often put the artist in the title: "Artist - Song (Official Video)".
        title_sim = max(title_sim, similarity(f"{track.main_artist} {track.title}", title))
        free_text = title
    dur = duration_score(track.duration_s, r.get("duration_seconds"), tolerance=12)
    if track.duration_s and r.get("duration_seconds") and abs(track.duration_s - r["duration_seconds"]) > 12:
        return 0.0  # different edit (extended mix, radio edit, live...)
    artist_sim = artist_overlap(track.artists, artists, free_text)
    score = 0.45 * title_sim + 0.35 * artist_sim + 0.20 * dur
    if from_isrc and title_sim > 0.8 and artist_sim > 0.5:
        score += 0.2  # YouTube Music indexed this exact recording under the same ISRC

    # Don't swap a version for another one: live, remix, acoustic, instrumental...
    found, wanted = f" {normalize(title)} ", f" {normalize(track.title)} "
    for word in ("live", "remix", "acoustic", "instrumental", "karaoke", "cover", "sped up", "slowed", "8d", "nightcore"):
        if (f" {word} " in found) != (f" {word} " in wanted):
            score -= 0.25
    if r.get("resultType") == "video":
        score -= 0.05  # prefer the clean studio audio over a video's audio
    if "isExplicit" in r and bool(r["isExplicit"]) != track.explicit:
        score -= 0.05  # clean vs explicit version
    return score


def find(track: Track, min_score: float = 0.72) -> Match | None:
    yt = _client()
    query = f"{' '.join(track.artists[:2])} {track.title}"
    searches = [(query, "songs"), (query, "videos")]
    if track.isrc:
        searches.insert(0, (track.isrc, "songs"))

    best: Match | None = None
    for q, filt in searches:
        try:
            results = yt.search(q, filter=filt, limit=10)
        except Exception:
            continue
        for r in results:
            if not r.get("videoId"):
                continue
            s = _score(track, r, from_isrc=q == track.isrc)
            if not best or s > best.score:
                best = _match(r, s, filt.rstrip("s"))
        if best and best.score >= 1.0:
            break  # ISRC-confirmed match, no need to keep searching
    return best if best and best.score >= min_score else None


def search(query: str, limit: int = 12) -> list[Match]:
    """What YouTube Music's own search shows, for picking a track by hand when find() comes up empty.
    Songs, then videos: only searches filtered like this report each result's length."""
    yt = _client()
    songs = yt.search(query, filter="songs", limit=limit)
    videos = yt.search(query, filter="videos", limit=limit)
    matches: dict[str, Match] = {}
    for r in songs[:limit * 2 // 3] + videos:
        if r.get("videoId"):
            matches.setdefault(r["videoId"], _match(r, 0.0))
    return list(matches.values())[:limit]
