from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class Track:
    spotify_id: str
    title: str
    artists: list[str]
    duration_ms: int = 0
    explicit: bool = False

    album: str | None = None
    album_artists: list[str] = field(default_factory=list)
    album_type: str | None = None
    track_number: int | None = None
    track_total: int | None = None
    disc_number: int | None = None
    disc_total: int | None = None
    release_date: str | None = None  # YYYY, YYYY-MM or YYYY-MM-DD

    isrc: str | None = None
    upc: str | None = None
    label: str | None = None
    copyright: str | None = None
    genres: list[str] = field(default_factory=list)
    bpm: float | None = None
    composers: list[str] = field(default_factory=list)
    lyricists: list[str] = field(default_factory=list)

    cover_url: str | None = None
    lyrics: str | None = None
    synced_lyrics: str | None = None  # LRC format

    deezer_id: int | None = None
    musicbrainz_recording_id: str | None = None
    musicbrainz_release_id: str | None = None
    musicbrainz_artist_ids: list[str] = field(default_factory=list)
    youtube_id: str | None = None

    @property
    def spotify_url(self) -> str:
        return f"https://open.spotify.com/track/{self.spotify_id}"

    @property
    def main_artist(self) -> str:
        return self.artists[0] if self.artists else ""

    @property
    def duration_s(self) -> float:
        return self.duration_ms / 1000

    def fill(self, **values) -> None:
        """Set fields that are still empty; never overwrite data we already have."""
        for key, value in values.items():
            if value in (None, "", [], 0):
                continue
            if getattr(self, key) in (None, "", [], 0):
                setattr(self, key, value)
