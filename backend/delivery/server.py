"""Read-only HTTP delivery for bijiSDK local development. No production hosting."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlsplit

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "publishing"))
from store import PublicationStore, context, encoded, integer  # noqa: E402

ROUTE = re.compile(r"/v1/projects/([^/]+)/environments/([^/]+)/config")


class ConfigRequestHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "bijiSDK-development/0.1"

    def _send(self, status, body=b"", etag=None):
        self.send_response(status)
        self.send_header("Cache-Control", "no-cache")
        if status != 304:
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
        if etag:
            self.send_header("ETag", etag)
        self.end_headers()
        if body and self.command != "HEAD":
            self.wfile.write(body)

    def do_GET(self):
        try:
            url = urlsplit(self.path)
            if url.path == "/health" and not url.query:
                self._send(200, encoded({"status": "ok", "service": "bijiSDK-development"}))
                return
            match = ROUTE.fullmatch(url.path)
            if match is None:
                self._send(404, encoded({"error": "Route not found."}))
                return
            query = parse_qs(url.query, keep_blank_values=True, strict_parsing=True, max_num_fields=10)
            required = {"platform", "appBuild", "configContractVersion"}
            if set(query) != required or any(len(values) != 1 for values in query.values()):
                raise ValueError("Exactly one platform, appBuild, and configContractVersion is required.")
            app_build = int(query["appBuild"][0])
            contract = int(query["configContractVersion"][0])
            project, environment, platform, contract = context(
                unquote(match[1]), unquote(match[2]), query["platform"][0], contract)
            integer(app_build, "appBuild")
            try:
                response = self.server.store.select(project, environment, platform, app_build, contract)
            except (OSError, ValueError, KeyError, TypeError):
                self._send(500, encoded({"error": "Published configuration cannot be read."}))
                return
            if response is None:
                self._send(404, encoded({"error": "No compatible published configuration."}))
                return
            body = encoded(response)
            etag = '"' + hashlib.sha256(body).hexdigest() + '"'
            supplied = self.headers.get("If-None-Match", "")
            tags = [tag.strip().removeprefix("W/") for tag in supplied.split(",")]
            if "*" in tags or etag in tags:
                self._send(304, etag=etag)
            else:
                self._send(200, body, etag)
        except (ValueError, UnicodeError) as error:
            self._send(400, encoded({"error": str(error)}))
        except (OSError, KeyError, TypeError):
            self._send(500, encoded({"error": "Published configuration cannot be read."}))

    def do_POST(self):
        self.close_connection = True
        self._send(405, encoded({"error": "This development server is read-only. Use the local publishing CLI."}))

    def log_message(self, message, *args):
        sys.stderr.write("%s %s\n" % (self.address_string(), message % args))


def create_server(data_dir, host="127.0.0.1", port=8765):
    server = ThreadingHTTPServer((host, port), ConfigRequestHandler)
    server.daemon_threads = True
    server.store = PublicationStore(data_dir)
    return server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path,
                        default=Path(__file__).resolve().parents[1] / "dev-data")
    parser.add_argument("--host", default="127.0.0.1",
                        help="Default is loopback. A LAN address must be selected explicitly.")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    if not 0 <= args.port <= 65535:
        parser.error("Port must be in 0..65535.")
    with create_server(args.data_dir, args.host, args.port) as server:
        print(json.dumps({"listening": f"http://{args.host}:{server.server_port}",
                          "dataDir": str(args.data_dir.resolve())}), flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
