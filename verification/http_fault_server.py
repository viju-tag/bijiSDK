"""Loopback-only fault responses for bijiSDK's Unity transport verification."""
from __future__ import annotations

import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit


class FaultHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def _respond(self, status, body=b"", headers=None):
        try:
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            for name, value in (headers or {}).items():
                self.send_header(name, value)
            self.end_headers()
            if body:
                self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError, OSError):
            # Cancellation and timeout intentionally disconnect some requests.
            self.close_connection = True

    def do_GET(self):
        path = urlsplit(self.path).path
        if path == "/health":
            self._respond(200, b'{"status":"ok"}')
        elif path == "/oversized":
            self._respond(200, b" " * 2048)
        elif path == "/invalid-utf8":
            self._respond(200, bytes((0xFF, 0xFE)))
        elif path == "/slow":
            time.sleep(1)
            self._respond(200, b'{"status":"delayed"}')
        elif path == "/redirect":
            self._respond(302, headers={"Location": "/health"})
        elif path == "/throttled":
            self._respond(429, b'{"error":"test throttle"}', {"Retry-After": "120"})
        else:
            self._respond(404, b'{"error":"not found"}')

    def do_POST(self):
        self.close_connection = True
        self._respond(405, b'{"error":"read only"}')

    def log_message(self, *_):
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", choices=("127.0.0.1",), default="127.0.0.1")
    parser.add_argument("--port", type=int, default=0)
    args = parser.parse_args()
    if not 0 <= args.port <= 65535:
        parser.error("Port must be between 0 and 65535.")
    with ThreadingHTTPServer((args.host, args.port), FaultHandler) as server:
        server.daemon_threads = True
        print(json.dumps({"listening": f"http://{args.host}:{server.server_port}"}), flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
