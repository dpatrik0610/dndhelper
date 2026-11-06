from __future__ import annotations

import re
import unicodedata
from difflib import SequenceMatcher

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

USER_AGENT = "SpotFetcher/0.1 (personal music tagger)"

session = requests.Session()
session.headers["User-Agent"] = USER_AGENT
_retry = Retry(total=4, backoff_factor=0.5, status_forcelist=(429, 500, 502, 503, 504), allowed_methods=None)
session.mount("https://", HTTPAdapter(max_retries=_retry))
session.mount("http://", HTTPAdapter(max_retries=_retry))

# Bracketed/dashed suffixes that streaming services add inconsistently.
_NOISE = re.compile(
    r"\s*[\(\[]?\s*[-–]?\s*\b("
    r"remaster(ed)?( \d{4})?|\d{4} remaster(ed)?|official (music )?(video|audio)|"
    r"lyrics?( video)?|audio|hd|hq|visuali[sz]er|explicit|clean"
    r")\b[^\)\]]*[\)\]]?",
    re.I,
)
_FEAT = re.compile(r"\s*[\(\[]?\s*\b(feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]?", re.I)


def strip_accents(s: str) -> str:
    return "".join(c for c in unicodedata.normalize("NFKD", s) if not unicodedata.combining(c))


def normalize(s: str, aggressive: bool = False) -> str:
    s = strip_accents(s).lower()
    if aggressive:
        s = _FEAT.sub(" ", s)
        s = _NOISE.sub(" ", s)
        s = re.sub(r"\s-\s.*$", "", s)  # "Song - 2011 Remaster" style suffixes
    s = s.replace("&", " and ")
    s = re.sub(r"[^\w\s]", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def clean_title(s: str) -> str:
    """Drop "(feat. X)", "(with X)", "- 2011 Remaster" etc. for search queries; keeps case."""
    s = _FEAT.sub(" ", s)
    s = re.sub(r"\s-\s.*\b(remaster(ed)?|version|edit|mix|mono|stereo)\b.*$", "", s, flags=re.I)
    return re.sub(r"\s+", " ", s).strip() or s


def similarity(a: str, b: str) -> float:
    """Best of a literal and a noise-stripped comparison, 0..1."""
    plain = SequenceMatcher(None, normalize(a), normalize(b)).ratio()
    loose = SequenceMatcher(None, normalize(a, True), normalize(b, True)).ratio()
    return max(plain, loose)


def artist_overlap(wanted: list[str], found: list[str], free_text: str = "") -> float:
    """How well the found artist credits cover the wanted ones, 0..1.

    Credits must match a whole name ("Pink Floyd Tribute Band" is not "Pink Floyd").
    `free_text` (e.g. a video title) may contain the name anywhere as whole words.
    """
    if not wanted:
        return 0.0
    found_n = [normalize(f) for f in found if f]
    text = f" {normalize(free_text)} "

    def present(name: str) -> bool:
        n = normalize(name)
        return bool(n) and (any(n == f or SequenceMatcher(None, n, f).ratio() > 0.9 for f in found_n) or f" {n} " in text)

    hits = sum(present(w) for w in wanted)
    # The main artist matters most.
    return 0.6 * present(wanted[0]) + 0.4 * hits / len(wanted)


def duration_score(want_s: float, got_s: float | None, tolerance: float = 15) -> float:
    if not want_s or not got_s:
        return 0.5
    diff = abs(want_s - got_s)
    return max(0.0, 1 - diff / tolerance)


_ILLEGAL = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


def safe_filename(s: str, max_len: int = 150) -> str:
    s = _ILLEGAL.sub("_", s).strip().rstrip(". ")
    if s.upper() in {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(10)), *(f"LPT{i}" for i in range(10))}:
        s = f"_{s}"
    return s[:max_len] or "untitled"
