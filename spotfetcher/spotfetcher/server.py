"""The helper the API talks to: a small web server on the same machine, never exposed.

It finds and downloads songs and hands each finished MP3 (and its cover) to the API, which stores it.
It holds no site credentials. Browsers cannot use it: any request carrying an Origin is refused.
"""
from __future__ import annotations

import json
import os
import queue
import re
import secrets
import shutil
import tempfile
import threading
from dataclasses import asdict
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from mutagen.mp3 import MP3

from . import __version__, spotify, tagger, ytmusic
from .core import Options, process_track
from .download import BITRATES, DownloadError, ffmpeg_path

PORT = 47821
_TRACK_ID = re.compile(r"^[A-Za-z0-9]{22}$")
_MAX_BODY = 64 * 1024

_work_dir = Path(tempfile.mkdtemp(prefix="spotfetcher-helper-"))
_files: dict[str, tuple[Path, str]] = {}  # single-use token -> (finished file, content type)
_found: dict[str, ytmusic.Match] = {}  # what searches returned: the only videos a page may pick


def request_allowed(origin: str | None, host: str | None, port: int = PORT) -> bool:
    """Only the API on this machine: browsers always send Origin on these requests, so no page gets in.
    The Host check stops a site pointing its own domain name at 127.0.0.1 (DNS rebinding)."""
    return origin is None and host in {f"127.0.0.1:{port}", f"localhost:{port}"}


def _has_ffmpeg() -> bool:
    try:
        ffmpeg_path()
        return True
    except DownloadError:
        return False


class Handler(BaseHTTPRequestHandler):
    server_version = f"SpotFetcher/{__version__}"

    def log_message(self, *_args) -> None:
        pass

    def _allowed(self) -> bool:
        if request_allowed(self.headers.get("Origin"), self.headers.get("Host")):
            return True
        print(f"Refused a request from {self.headers.get('Origin') or self.headers.get('Host')}.")
        self._send_json(403, {"error": "Only the API may use the helper."})
        return False

    def _send_json(self, status: int, body: dict) -> None:
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _body(self) -> dict:
        length = int(self.headers.get("Content-Length") or 0)
        if length > _MAX_BODY:
            raise ValueError("That request is too large.")
        body = json.loads(self.rfile.read(length) or b"{}")
        if not isinstance(body, dict):
            raise ValueError("That request is not understood.")
        return body

    def do_GET(self) -> None:
        if not self._allowed():
            return
        if self.path == "/health":
            return self._send_json(200, {"ffmpeg": _has_ffmpeg(), "version": __version__})
        if self.path.startswith("/file/"):
            return self._send_file(self.path.removeprefix("/file/"))
        self._send_json(404, {"error": "Nothing here."})

    def do_POST(self) -> None:
        if not self._allowed():
            return
        try:
            body = self._body()
        except ValueError as e:
            return self._send_json(400, {"error": str(e)})
        if self.path == "/resolve":
            return self._resolve(body)
        if self.path == "/search":
            return self._search(body)
        if self.path == "/fetch":
            return self._fetch(body)
        self._send_json(404, {"error": "Nothing here."})

    def _resolve(self, body: dict) -> None:
        try:
            _kind, name, ids = spotify.resolve(str(body.get("url") or ""))
        except spotify.SpotifyError as e:
            return self._send_json(400, {"error": str(e)})
        except Exception as e:
            return self._send_json(502, {"error": f"Could not read that link from Spotify: {e}"})
        tracks = [
            {"id": i, "title": spotify.previews.get(i, {}).get("title"), "artists": spotify.previews.get(i, {}).get("artists") or []}
            for i in ids
        ]
        self._send_json(200, {"name": name, "tracks": tracks})

    def _search(self, body: dict) -> None:
        query = body.get("query")
        if not isinstance(query, str) or not query.strip():
            return self._send_json(400, {"error": "That request is not understood."})
        try:
            found = ytmusic.search(query.strip()[:200])
        except Exception as e:
            print(f"  Search failed: {e}")
            return self._send_json(502, {"error": "The search failed. Check your connection and try again."})
        _found.update((m.video_id, m) for m in found)
        self._send_json(200, {"results": [asdict(m) for m in found]})

    def _fetch(self, body: dict) -> None:
        track_id, bitrate, max_bytes = body.get("id"), body.get("bitrate"), body.get("maxBytes")
        if not (isinstance(track_id, str) and _TRACK_ID.match(track_id)) or bitrate not in BITRATES:
            return self._send_json(400, {"error": "That request is not understood."})
        if not isinstance(max_bytes, int) or max_bytes <= 0:
            return self._send_json(400, {"error": "That request is not understood."})
        pick = None
        if body.get("pick") is not None:
            pick = _found.get(body["pick"]) if isinstance(body["pick"], str) else None
            if not pick:
                return self._send_json(400, {"error": "That search result is gone. Try again to search once more."})

        events: queue.Queue[dict] = queue.Queue()

        def stage(_track: str, name: str, data: dict) -> None:
            if name in ("metadata", "matching", "downloading", "converting", "tagging"):
                events.put({"stage": name, "progress": data.get("progress"), "title": data.get("title"), "artists": data.get("artists")})

        def work() -> None:
            try:
                out = Path(tempfile.mkdtemp(dir=_work_dir))
                opts = Options(output_dir=out, bitrate=bitrate, musicbrainz=False, overwrite=True, max_bytes=max_bytes, cookies=os.getenv("SPOTFETCHER_COOKIES"))
                result = process_track(track_id, opts, log=print, emit=stage, pick=pick)
                if result.path:
                    t = result.track
                    events.put({
                        "file": _share(result.path, "audio/mpeg"),
                        "name": result.path.name,
                        "title": t.title,
                        "artists": t.artists,
                        "duration": MP3(result.path).info.length or t.duration_s,
                        "cover": _cover(t.cover_url),
                    })
                elif result.status == "not_found":
                    events.put({"notFound": True})
                else:
                    events.put({"error": result.message or "It could not be fetched."})
            except Exception as e:
                print(f"  ERROR: {e}")
                events.put({"error": str(e)})

        threading.Thread(target=work, daemon=True).start()
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        try:
            while True:
                event = events.get()
                self.wfile.write(json.dumps(event).encode() + b"\n")
                self.wfile.flush()
                if "stage" not in event:  # a file, an error or no match: this song is over
                    return
        except (BrokenPipeError, ConnectionError):
            pass  # the page went away; the finished file is cleaned up when the helper closes

    def _send_file(self, token: str) -> None:
        entry = _files.pop(token, None)
        if not entry:
            return self._send_json(404, {"error": "That file is gone."})
        path, content_type = entry
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(path.stat().st_size))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        try:
            with path.open("rb") as f:
                shutil.copyfileobj(f, self.wfile)
        except (BrokenPipeError, ConnectionError):
            pass
        finally:
            shutil.rmtree(path.parent, ignore_errors=True)


def _share(path: Path, content_type: str) -> str:
    token = secrets.token_urlsafe(16)
    _files[token] = (path, content_type)
    return f"/file/{token}"


def _cover(url: str | None) -> str | None:
    found = tagger.fetch_cover(url)
    if not found:
        return None
    data, mime = found
    path = Path(tempfile.mkdtemp(dir=_work_dir)) / "cover"
    path.write_bytes(data)
    return _share(path, mime)


def main() -> None:
    # Bound to this machine only: nothing on the network can reach it, only the API beside it.
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    print(f"SpotFetcher helper {__version__} is running for the API on 127.0.0.1:{PORT}.")
    if not _has_ffmpeg():
        print("ffmpeg was not found. Reinstall requirements.txt.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        shutil.rmtree(_work_dir, ignore_errors=True)
