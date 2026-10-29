import json
import threading
import unittest
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from unittest.mock import patch
from battery_core import Reading
from web_bridge import make_server


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.server, self.url = make_server(Path(__file__).resolve().parent)
        self.worker = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.worker.start()
    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.worker.join()
    def test_missing_token_rejected(self):
        with self.assertRaises(HTTPError) as caught:
            urlopen(self.url.split('?')[0])
        self.assertEqual(caught.exception.code, 403)
    def test_foreign_origin_rejected(self):
        req = Request(self.url, headers={'Origin': 'https://example.com'})
        with self.assertRaises(HTTPError) as caught: urlopen(req)
        self.assertEqual(caught.exception.code, 403)
    @patch('web_bridge.Monitor.scan', return_value=[Reading('test','Test mouse','Test',75)])
    def test_api_and_cache(self, scan):
        url = self.url.replace('/?', '/api/battery?')
        for _ in range(2):
            with urlopen(url) as res: data = json.load(res)
            self.assertEqual(data['readings'][0]['percent'],75)
        scan.assert_called_once()
    def test_page(self):
        with urlopen(self.url) as res:
            self.assertEqual(res.status,200)
            html = res.read().decode()
        self.assertIn('WebHID',html)
        self.assertNotIn('__CATALOG__',html)


if __name__ == '__main__': unittest.main()
