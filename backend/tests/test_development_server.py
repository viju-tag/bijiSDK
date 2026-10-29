"""Repeatable local publisher and HTTP checks; no external service or third-party packages."""
from __future__ import annotations

import copy
import http.client
import importlib.util
import json
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch

BACKEND = Path(__file__).resolve().parents[1]
ROOT = BACKEND.parent
sys.path.insert(0, str(BACKEND / "publishing"))
from store import PublicationStore, read_json, validate_response

spec = importlib.util.spec_from_file_location("biji_development_server", BACKEND / "delivery/server.py")
server_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(server_module)


class DevelopmentServerTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="bijisdk-test-", dir=BACKEND / "tests")
        self.store = PublicationStore(self.directory.name)
        self.template = read_json(ROOT / "contracts/examples/valid-config.json")
        self.template["platform"] = "editor"
        self.server = server_module.create_server(self.directory.name, port=0)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)
        self.directory.cleanup()

    def get(self, suffix="", headers=None, platform="editor", build=120, environment="production"):
        path = (f"/v1/projects/example-game/environments/{environment}/config"
                f"?platform={platform}&appBuild={build}&configContractVersion=1{suffix}")
        connection = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=5)
        try:
            connection.request("GET", path, headers=headers or {})
            response = connection.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            connection.close()

    def publish(self, **kwargs):
        return self.store.publish(self.template, "Automated test.", **kwargs)

    def test_delivery_and_etag(self):
        publication = self.publish()
        status, headers, body = self.get()
        self.assertEqual(200, status)
        self.assertEqual(publication["revision"], json.loads(body)["revision"])
        self.assertEqual(str(len(body)), headers["Content-Length"])
        self.assertEqual("no-cache", headers["Cache-Control"])
        status, response_headers, body = self.get(headers={"If-None-Match": headers["ETag"]})
        self.assertEqual(304, status)
        self.assertEqual(b"", body)
        self.assertEqual(headers["ETag"], response_headers["ETag"])

    def test_weak_and_multiple_etags(self):
        self.publish()
        _, headers, _ = self.get()
        status, _, body = self.get(headers={"If-None-Match": '"other", W/' + headers["ETag"]})
        self.assertEqual((304, b""), (status, body))

    def test_unpublished_is_not_visible(self):
        self.assertEqual(404, self.get()[0])

    def test_context_and_compatibility_isolation(self):
        self.publish()
        self.assertEqual(404, self.get(platform="android")[0])
        self.assertEqual(404, self.get(environment="qa")[0])
        self.assertEqual(404, self.get(build=200)[0])

    def test_newest_compatible_revision_is_selected(self):
        original = self.publish()
        self.template["compatibility"] = {"minAppBuild": 200, "maxAppBuild": None}
        newer = self.publish()
        self.assertEqual(original["revision"], json.loads(self.get(build=120)[2])["revision"])
        self.assertEqual(newer["revision"], json.loads(self.get(build=200)[2])["revision"])

    def test_publish_then_rollback_gets_new_revision(self):
        first = self.publish()
        first_etag = self.get()[1]["ETag"]
        self.template["values"]["energy"]["maximum"] = 150
        second = self.publish()
        self.assertEqual(150, json.loads(self.get()[2])["values"]["energy"]["maximum"])
        rolled = self.store.rollback("example-game", "production", "editor", 1,
                                     first["revision"], "Undo test.", expected_revision=second["revision"])
        status, headers, body = self.get()
        self.assertEqual(200, status)
        self.assertEqual(100, json.loads(body)["values"]["energy"]["maximum"])
        self.assertNotEqual(first["revision"], rolled["revision"])
        self.assertNotEqual(first_etag, headers["ETag"])
        context_dir = next((Path(self.directory.name) / "contexts").iterdir())
        audit = read_json(context_dir / "publications" / (rolled["revision"] + ".json"))["audit"]
        self.assertEqual(first["revision"], audit["rollbackFrom"])
        self.assertEqual(second["revision"], audit["previousRevision"])

    def test_invalid_publication_preserves_current(self):
        first = self.publish()
        self.template["schemaVersion"] = 2
        with self.assertRaises(ValueError):
            self.publish()
        self.assertEqual(first["revision"], json.loads(self.get()[2])["revision"])

    def test_optional_feature_validation(self):
        self.template["values"]["energy"]["maximum"] = -1
        with self.assertRaises(ValueError):
            self.publish(example_features=True)
        self.assertEqual(404, self.get()[0])

    def test_publish_precondition_rejects_changed_pointer(self):
        first = self.publish()
        second = self.publish()
        with self.assertRaises(ValueError):
            self.publish(expected_revision=first["revision"])
        self.assertEqual(second["revision"], json.loads(self.get()[2])["revision"])

    def test_failed_pointer_swap_does_not_publish_orphan(self):
        first = self.publish()
        with patch("store.os.replace", side_effect=OSError("Injected replace failure.")):
            with self.assertRaises(OSError):
                self.publish()
        self.assertEqual(first["revision"], json.loads(self.get()[2])["revision"])

    def test_duplicate_or_invalid_query_is_rejected(self):
        self.assertEqual(400, self.get(suffix="&appBuild=120")[0])
        self.assertEqual(400, self.get(build=-1)[0])
        self.assertEqual(400, self.get(suffix="&unexpected=x")[0])

    def test_bad_published_record_is_server_error(self):
        first = self.publish()
        context_dir = next((Path(self.directory.name) / "contexts").iterdir())
        (context_dir / "publications" / (first["revision"] + ".json")).write_text("{broken")
        self.assertEqual(500, self.get()[0])

    def test_strict_json_and_numeric_validation(self):
        path = Path(self.directory.name) / "invalid.json"
        path.write_text('{"schemaVersion":1,"schemaVersion":1}')
        with self.assertRaises(ValueError):
            read_json(path)
        for value in (float("nan"), float("inf"), 2 ** 53):
            candidate = copy.deepcopy(self.template)
            candidate["values"]["unknown"] = value
            with self.assertRaises(ValueError):
                validate_response(candidate)
        candidate = copy.deepcopy(self.template)
        candidate["compatibility"]["maxAppBuild"] = 99
        with self.assertRaises(ValueError):
            validate_response(candidate)

    def test_publish_lock_prevents_competing_writer(self):
        first = self.publish()
        directory = next((Path(self.directory.name) / "contexts").iterdir())
        lock = directory / "publish.lock"
        lock.write_text("existing publisher")
        try:
            with self.assertRaises(ValueError):
                self.publish()
        finally:
            lock.unlink()
        self.assertEqual(first["revision"], json.loads(self.get()[2])["revision"])

    def test_read_only_http(self):
        connection = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=5)
        try:
            connection.request("POST", "/publish", body=b"{}")
            response = connection.getresponse()
            self.assertEqual(405, response.status)
            response.read()
        finally:
            connection.close()


if __name__ == "__main__":
    unittest.main(verbosity=2)
