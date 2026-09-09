import base64
from io import BytesIO
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'engine'))
from elective_orb_core.captcha.collection import collect_existing_image
from elective_orb_core.captcha import TTShituRecognizer


class CollectionTests(unittest.TestCase):
    def test_disabled_enabled_dedup_and_immediate_disable(self):
        output = BytesIO()
        Image.new('RGB', (80, 30), 'white').save(output, 'PNG')
        raw = output.getvalue()
        encoded = TTShituRecognizer._encode_image(raw)
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'AUTOELECTIVE_DATA_DIR': directory}):
            root = Path(directory)
            self.assertFalse(collect_existing_image(raw, encoded))
            self.assertFalse((root / 'captcha-collection').exists())
            marker = root / 'collect-captcha.enabled'
            marker.touch()
            self.assertTrue(collect_existing_image(raw, encoded))
            self.assertTrue(collect_existing_image(raw, encoded))
            originals = list((root / 'captcha-collection/original').iterdir())
            uploaded = list((root / 'captcha-collection/uploaded').iterdir())
            self.assertEqual(len(originals), 1)
            self.assertEqual(originals[0].read_bytes(), raw)
            self.assertEqual(uploaded[0].read_bytes(), base64.b64decode(encoded))
            marker.unlink()
            self.assertFalse(collect_existing_image(raw, encoded))

    def test_write_failure_does_not_raise(self):
        output = BytesIO()
        Image.new('RGB', (20, 20)).save(output, 'PNG')
        raw = output.getvalue()
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'AUTOELECTIVE_DATA_DIR': directory}):
            (Path(directory) / 'collect-captcha.enabled').touch()
            with patch('elective_orb_core.captcha.collection._atomic_write', side_effect=OSError):
                self.assertFalse(collect_existing_image(raw, TTShituRecognizer._encode_image(raw)))

    def test_recognition_still_uses_only_one_upload(self):
        from test_captcha import FakeSession, FakeResponse
        output = BytesIO()
        Image.new('RGB', (20, 20)).save(output, 'PNG')
        session = FakeSession(FakeResponse({'success': True, 'data': {'result': 'Ab123'}}))
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'AUTOELECTIVE_DATA_DIR': directory}):
            (Path(directory) / 'collect-captcha.enabled').touch()
            result = TTShituRecognizer('test', 'test', session).recognize(output.getvalue())
            self.assertEqual(result.code, 'Ab123')
            self.assertEqual(len(session.calls), 1)
            self.assertEqual(len(list((Path(directory) / 'captcha-collection/uploaded').iterdir())), 1)
