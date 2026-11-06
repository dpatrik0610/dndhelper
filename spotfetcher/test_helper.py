"""python -m unittest test_helper (from this folder)"""
import http.client
import json
import tempfile
import threading
import unittest
from http.server import ThreadingHTTPServer
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from spotfetcher import core, server, ytmusic
from spotfetcher.core import fit_bitrate
from spotfetcher.models import Track
from spotfetcher.server import PORT, request_allowed

MB = 1024 * 1024


class FitBitrate(unittest.TestCase):
    def test_keeps_the_chosen_quality_when_it_fits(self):
        self.assertEqual(fit_bitrate(320, 180, 15 * MB), 320)

    def test_never_raises_the_quality(self):
        self.assertEqual(fit_bitrate(128, 60, 15 * MB), 128)

    def test_drops_to_what_fits(self):
        self.assertEqual(fit_bitrate(320, 420, 15 * MB), 256)  # 7 minutes
        self.assertEqual(fit_bitrate(320, 700, 15 * MB), 128)

    def test_says_when_nothing_fits(self):
        with self.assertRaises(ValueError):
            fit_bitrate(320, 1200, 15 * MB)

    def test_unknown_length_keeps_the_choice(self):
        self.assertEqual(fit_bitrate(320, 0, 15 * MB), 320)


class Requests(unittest.TestCase):
    site = "https://site.example"
    origins = {site}
    host = f"127.0.0.1:{PORT}"

    def test_the_site(self):
        self.assertTrue(request_allowed(self.site, self.host, self.origins))
        self.assertTrue(request_allowed(self.site, f"localhost:{PORT}", self.origins))

    def test_another_page(self):
        self.assertFalse(request_allowed("https://evil.example", self.host, self.origins))

    def test_no_origin(self):
        self.assertFalse(request_allowed(None, self.host, self.origins))

    def test_a_domain_pointed_at_this_computer(self):
        self.assertFalse(request_allowed(self.site, f"evil.example:{PORT}", self.origins))


class Search(unittest.TestCase):
    def test_keeps_only_playable_results_once_each(self):
        by_filter = {
            "songs": [
                {"resultType": "song", "videoId": "a", "title": "One", "artists": [{"name": "Band"}], "duration_seconds": 200},
                {"resultType": "song", "videoId": None, "title": "Unplayable"},
            ],
            "videos": [
                {"resultType": "video", "videoId": "a", "title": "One again"},
                {"resultType": "video", "videoId": "b", "title": "Live", "artists": None, "duration_seconds": 210},
            ],
        }
        with mock.patch.object(ytmusic, "_client") as client:
            client.return_value.search.side_effect = lambda _q, filter, limit: by_filter[filter]
            found = ytmusic.search("one")
        self.assertEqual([(m.video_id, m.kind, m.duration_s) for m in found], [("a", "song", 200), ("b", "video", 210)])

    def test_leaves_room_for_videos(self):
        rows = lambda kind: [{"resultType": kind, "videoId": f"{kind}{i}", "title": "x"} for i in range(12)]
        with mock.patch.object(ytmusic, "_client") as client:
            client.return_value.search.side_effect = lambda _q, filter, limit: rows(filter.rstrip("s"))
            found = ytmusic.search("one")
        self.assertEqual(len(found), 12)
        self.assertEqual(sum(m.kind == "video" for m in found), 4)


class Pick(unittest.TestCase):
    def run_track(self, pick, spotify_seconds=180):
        track = Track("id", "Spotify Title", ["Artist"], duration_ms=spotify_seconds * 1000)
        with tempfile.TemporaryDirectory() as tmp, \
                mock.patch.object(core, "gather_metadata", return_value=track), \
                mock.patch.object(core.ytmusic, "find") as find, \
                mock.patch.object(core, "download_mp3", return_value={}) as download, \
                mock.patch.object(core.tagger, "tag"):
            result = core.process_track("id", core.Options(output_dir=Path(tmp), max_bytes=15 * MB), lambda _: None, pick=pick)
        return result, find, download

    def test_skips_the_search_and_keeps_spotifys_name(self):
        result, find, download = self.run_track(ytmusic.Match("chosen", "YouTube Title", ["Artist"], 180, "song", 0.0))
        find.assert_not_called()
        self.assertEqual(download.call_args.args[0], "chosen")
        self.assertEqual(result.path.stem, "Artist - Spotify Title")

    def test_a_longer_pick_drops_the_quality_to_fit(self):
        _result, _find, download = self.run_track(ytmusic.Match("chosen", "Long", ["Artist"], 420, "video", 0.0))
        self.assertEqual(download.call_args.args[2], 256)


class Routes(unittest.TestCase):
    track = "A" * 22

    def setUp(self):
        self.site = ThreadingHTTPServer(("127.0.0.1", 0), server.Handler)
        threading.Thread(target=self.site.serve_forever, daemon=True).start()
        self.addCleanup(self.site.server_close)
        self.addCleanup(self.site.shutdown)
        self.addCleanup(server._found.clear)

    def post(self, path, body):
        conn = http.client.HTTPConnection("127.0.0.1", self.site.server_address[1])
        self.addCleanup(conn.close)
        conn.request("POST", path, json.dumps(body), {"Origin": "http://localhost:3000", "Host": f"127.0.0.1:{PORT}"})
        res = conn.getresponse()
        return res.status, res.read().decode()

    def fetch(self, **extra):
        return self.post("/fetch", {"id": self.track, "bitrate": 320, "maxBytes": 15 * MB, **extra})

    def test_search_lists_the_results(self):
        with mock.patch.object(ytmusic, "search", return_value=[ytmusic.Match("vid", "One", ["Band"], 200, "song", 0.0)]):
            status, text = self.post("/search", {"query": "one"})
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(text)["results"][0]["video_id"], "vid")

    def test_search_needs_words(self):
        self.assertEqual(self.post("/search", {"query": "  "})[0], 400)
        self.assertEqual(self.post("/search", {"query": 5})[0], 400)

    def test_no_match_says_so_and_ends(self):
        missed = SimpleNamespace(path=None, status="not_found", message="")
        with mock.patch.object(server, "process_track", return_value=missed):
            status, text = self.fetch()
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(text.strip()), {"notFound": True})

    def test_only_a_searched_video_can_be_picked(self):
        with mock.patch.object(server, "process_track") as process:
            self.assertEqual(self.fetch(pick="never-searched")[0], 400)
            self.assertEqual(self.fetch(pick=["vid"])[0], 400)
        process.assert_not_called()

    def test_a_searched_video_reaches_the_download(self):
        match = ytmusic.Match("vid", "One", ["Band"], 200, "song", 0.0)
        server._found[match.video_id] = match
        missed = SimpleNamespace(path=None, status="error", message="stop here")
        with mock.patch.object(server, "process_track", return_value=missed) as process:
            self.fetch(pick="vid")
        self.assertIs(process.call_args.kwargs["pick"], match)


if __name__ == "__main__":
    unittest.main()
