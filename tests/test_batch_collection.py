from io import BytesIO
from pathlib import Path
import sys
import tempfile
import unittest

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'engine'))
from collect_captchas import collect


class Response:
    def __init__(self, raw, status=200, mime='image/png'):
        self.raw = raw
        self.status_code = status
        self.headers = {'Content-Type': mime}
        self.closed = False

    def iter_content(self, _):
        yield self.raw

    def close(self):
        self.closed = True


def image(color):
    out = BytesIO()
    Image.new('RGB', (40, 20), color).save(out, 'PNG')
    return out.getvalue()


class BatchTests(unittest.TestCase):
    def test_target_interval_and_preservation(self):
        responses = [Response(image('red')), Response(image('blue'))]
        queue = iter(responses)
        sleeps = []
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(collect(lambda: next(queue), directory, 2, 1, lambda _: None, sleeps.append), 2)
            self.assertEqual(len(list((Path(directory) / 'batch-original').iterdir())), 2)
            self.assertEqual(sleeps, [1])
            self.assertTrue(all(r.closed for r in responses))

    def test_duplicates_stop_at_attempt_limit(self):
        calls = []
        def fetch():
            calls.append(1)
            return Response(image('red'))
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(collect(fetch, directory, 2, 1, lambda _: None, lambda _: None), 1)
        self.assertEqual(len(calls), 6)

    def test_auth_redirect_and_rate_limit_stop_without_retry(self):
        for status in [302, 401, 403, 429]:
            calls = []
            def fetch():
                calls.append(1)
                return Response(b'', status)
            with tempfile.TemporaryDirectory() as directory:
                self.assertEqual(collect(fetch, directory, 3, 1, lambda _: None, lambda _: None), 0)
            self.assertEqual(len(calls), 1)

    def test_three_bad_images_stop(self):
        calls = []
        def fetch():
            calls.append(1)
            return Response(b'bad')
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(collect(fetch, directory, 5, 1, lambda _: None, lambda _: None), 0)
        self.assertEqual(len(calls), 3)

    def test_html_and_invalid_limits(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(collect(lambda: Response(b'login', mime='text/html'), directory, 1, 1), 0)
            with self.assertRaises(ValueError):
                collect(lambda: None, directory, 300, 0)
