"""The helper the site's SpotFetcher tab talks to: a small web server on this computer only.

It finds and downloads songs and hands each finished MP3 to the browser, which uploads it with the
DM's own sign-in. So it holds no site credentials. Only pages from SPOTFETCHER_ORIGINS may use it.
"""
from __future__ import annotations

import json
import os
import queue
import re
import secrets
import shutil
import sys
import tempfile
import threading
from dataclasses import asdict
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from . import __version__, spotify, ytmusic
from .core import Options, process_track
from .download import BITRATES, DownloadError, ffmpeg_path

PORT = 47821
DEFAULT_ORIGINS = "https://dndreforged.quest,https://www.dndreforged.quest,http://localhost:3000"
_TRACK_ID = re.compile(r"^[A-Za-z0-9]{22}$")
_MAX_BODY = 64 * 1024

_work_dir = Path(tempfile.mkdtemp(prefix="spotfetcher-helper-"))
_files: dict[str, tuple[Path, str]] = {}  # single-use token -> (finished file, name)
_found: dict[str, ytmusic.Match] = {}  # what searches returned: the only videos a page may pick


def allowed_origins() -> set[str]:
    return {o.strip().rstrip("/") for o in os.getenv("SPOTFETCHER_ORIGINS", DEFAULT_ORIGINS).split(",") if o.strip()}


def request_allowed(origin: str | None, host: str | None, origins: set[str], port: int = PORT) -> bool:
    """Browsers always send Origin on these requests, so another site's page, or a plain link or image, gets nothing.
    The Host check stops a site pointing its own domain name at 127.0.0.1 (DNS rebinding)."""
    return origin in origins and host in {f"127.0.0.1:{port}", f"localhost:{port}"}


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
        if request_allowed(self.headers.get("Origin"), self.headers.get("Host"), allowed_origins()):
            return True
        print(f"Refused a request from {self.headers.get('Origin')}. Add it to SPOTFETCHER_ORIGINS if it is your site.")
        self._send_json(403, {"error": "This page is not allowed to use the helper."}, cors=False)
        return False

    def _cors(self) -> None:
        self.send_header("Access-Control-Allow-Origin", self.headers["Origin"])
        self.send_header("Vary", "Origin")

    def _send_json(self, status: int, body: dict, cors: bool = True) -> None:
        data = json.dumps(body).encode()
        self.send_response(status)
        if cors:
            self._cors()
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

    def do_OPTIONS(self) -> None:
        if not self._allowed():
            return
        self.send_response(204)
        self._cors()
        self.send_header("Access-Control-Allow-Methods", "GET, POST")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Private-Network", "true")  # lets a public site reach this computer
        self.send_header("Access-Control-Max-Age", "600")
        self.end_headers()

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
                    token = secrets.token_urlsafe(16)
                    _files[token] = (result.path, result.path.name)
                    events.put({"file": f"/file/{token}", "name": result.path.name})
                elif result.status == "not_found":
                    events.put({"notFound": True})
                else:
                    events.put({"error": result.message or "It could not be fetched."})
            except Exception as e:
                print(f"  ERROR: {e}")
                events.put({"error": str(e)})

        threading.Thread(target=work, daemon=True).start()
        self.send_response(200)
        self._cors()
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
        path, _name = entry
        self.send_response(200)
        self._cors()
        self.send_header("Content-Type", "audio/mpeg")
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


def main() -> None:
    # Bound to this computer only: nothing on the network can reach it.
    try:
        server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    except OSError:
        print("SpotFetcher is already running in another window, or something else uses its port.")
        if getattr(sys, "frozen", False):
            input("Press Enter to close.")
        return
    print(f"SpotFetcher helper {__version__} is running. Leave this window open while you fetch songs; close it when you are done.")
    print(f"Pages allowed to use it: {', '.join(sorted(allowed_origins()))}")
    if not _has_ffmpeg():
        print("ffmpeg was not found. Reinstall SpotFetcher.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        shutil.rmtree(_work_dir, ignore_errors=True)
