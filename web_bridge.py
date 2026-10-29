"""Loopback-only, token-protected read-only battery bridge."""
import json
import secrets
import threading
import time
import webbrowser
from dataclasses import asdict
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit
from battery_core import Monitor


def make_server(resources, port=0):
    token = secrets.token_urlsafe(32)
    monitor = Monitor(resources)
    lock = threading.Lock()
    cache = {'updated': 0, 'readings': [], 'errors': []}

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args): pass
        def do_GET(self):
            parts = urlsplit(self.path)
            expected_host = f'127.0.0.1:{self.server.server_port}'
            if self.headers.get('Host') != expected_host:
                self.send_error(403); return
            origin = self.headers.get('Origin')
            if origin and origin != 'http://' + expected_host:
                self.send_error(403); return
            if parse_qs(parts.query).get('token', [''])[0] != token:
                self.send_error(403); return
            if parts.path == '/':
                data = (resources / 'web' / 'index.html').read_bytes()
                kind = 'text/html; charset=utf-8'
            elif parts.path == '/api/battery':
                with lock:
                    if time.time() - cache['updated'] >= 10:
                        cache['readings'] = [asdict(r) for r in monitor.scan()]
                        cache['errors'] = monitor.errors
                        cache['updated'] = time.time()
                    data = json.dumps(cache, ensure_ascii=False).encode('utf-8')
                kind = 'application/json; charset=utf-8'
            elif parts.path == '/api/stop':
                data = b'{"stopped":true}'
                kind = 'application/json'
                threading.Thread(target=self.server.shutdown, daemon=True).start()
            else:
                self.send_error(404); return
            self.send_response(200)
            self.send_header('Content-Type', kind)
            self.send_header('Content-Length', str(len(data)))
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Referrer-Policy', 'no-referrer')
            self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'")
            self.end_headers()
            self.wfile.write(data)

    server = ThreadingHTTPServer(('127.0.0.1', port), Handler)
    return server, f'http://127.0.0.1:{server.server_port}/?token={token}'


def serve(resources):
    server, url = make_server(resources)
    webbrowser.open(url)
    try:
        server.serve_forever()
    finally:
        server.server_close()
