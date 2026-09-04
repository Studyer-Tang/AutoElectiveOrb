import base64
from io import BytesIO
import os
import sys
import unittest

import requests
from PIL import Image


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "engine"))

from elective_orb_core.captcha import TTShituRecognizer  # noqa: E402
from elective_orb_core.exceptions import RecognizerError  # noqa: E402


class FakeResponse:
    def __init__(self, body=None, json_error=None, http_error=None):
        self.body = body
        self.json_error = json_error
        self.http_error = http_error

    def raise_for_status(self):
        if self.http_error:
            raise self.http_error

    def json(self):
        if self.json_error:
            raise self.json_error
        return self.body


class FakeSession:
    def __init__(self, response=None, error=None):
        self.response = response
        self.error = error
        self.calls = []

    def post(self, url, **options):
        self.calls.append((url, options))
        if self.error:
            raise self.error
        return self.response


class CaptchaRecognizerTests(unittest.TestCase):
    @staticmethod
    def _image_bytes(color="white", image_format="PNG"):
        output = BytesIO()
        Image.new("RGB", (80, 30), color).save(output, format=image_format)
        return output.getvalue()

    @staticmethod
    def _animated_gif_bytes():
        output = BytesIO()
        first = Image.new("RGB", (80, 30), "red")
        last = Image.new("RGB", (80, 30), "blue")
        first.save(output, format="GIF", save_all=True, append_images=[last], duration=20, loop=0)
        return output.getvalue()

    def setUp(self):
        self.image = self._image_bytes()

    def test_recognizes_one_five_character_ascii_result(self):
        session = FakeSession(FakeResponse({
            "success": True,
            "code": "0",
            "data": {"result": " A8b2Z ", "id": "request-id"},
        }))
        captcha = TTShituRecognizer("test-user", "test-password", session).recognize(self.image)
        self.assertEqual(captcha.code, "A8b2Z")
        self.assertIsNone(captcha.confidence)
        self.assertEqual(captcha.engine, "ttshitu")
        url, options = session.calls[0]
        self.assertEqual(url, "https://api.ttshitu.com/base64")
        self.assertEqual(options["json"]["typeid"], 1003)
        uploaded = base64.b64decode(options["json"]["image"])
        with Image.open(BytesIO(uploaded)) as normalized:
            self.assertEqual(normalized.format, "JPEG")
            self.assertEqual(normalized.mode, "RGB")
        self.assertEqual(options["timeout"], (5, 60))

    def test_uses_last_frame_of_animated_captcha(self):
        response = FakeResponse({"success": True, "data": {"result": "AB12Z"}})
        session = FakeSession(response)
        TTShituRecognizer("user", "secret", session).recognize(self._animated_gif_bytes())
        uploaded = base64.b64decode(session.calls[0][1]["json"]["image"])
        with Image.open(BytesIO(uploaded)) as normalized:
            red, _green, blue = normalized.convert("RGB").getpixel((40, 15))
        self.assertGreater(blue, red)

    def test_rejects_non_ascii_and_wrong_length_results(self):
        for text in ("验证码测试", "ABCD", "ABCDEF", "A-123"):
            with self.subTest(text=text), self.assertRaises(RecognizerError):
                response = FakeResponse({"success": True, "data": {"result": text}})
                TTShituRecognizer("user", "secret", FakeSession(response)).recognize(self.image)

    def test_rejects_empty_and_oversized_images_without_request(self):
        session = FakeSession(FakeResponse({"success": True, "data": {"result": "AB12Z"}}))
        recognizer = TTShituRecognizer("user", "secret", session)
        for raw in (b"", b"x" * (TTShituRecognizer.MAX_IMAGE_BYTES + 1)):
            with self.subTest(size=len(raw)), self.assertRaises(RecognizerError):
                recognizer.recognize(raw)
        self.assertEqual(session.calls, [])

    def test_rejects_malformed_image_without_request(self):
        session = FakeSession(FakeResponse({"success": True, "data": {"result": "AB12Z"}}))
        with self.assertRaisesRegex(RecognizerError, "无法解析"):
            TTShituRecognizer("user", "secret", session).recognize(b"not-an-image")
        self.assertEqual(session.calls, [])

    def test_maps_service_and_network_errors_without_leaking_credentials(self):
        cases = (
            (FakeSession(FakeResponse({"success": False, "message": "用户名或密码错误: test-password"})), "账号或密码错误"),
            (FakeSession(FakeResponse({"success": False, "message": "余额不足"})), "余额不足"),
            (FakeSession(error=requests.Timeout("test-password")), "请求超时"),
            (FakeSession(error=requests.ConnectionError("test-password")), "无法连接"),
        )
        for session, expected in cases:
            with self.subTest(expected=expected), self.assertRaises(RecognizerError) as raised:
                TTShituRecognizer("test-user", "test-password", session).recognize(self.image)
            message = str(raised.exception)
            self.assertIn(expected, message)
            self.assertNotIn("test-user", message)
            self.assertNotIn("test-password", message)

    def test_redacts_credentials_from_unknown_service_error(self):
        response = FakeResponse({
            "success": False,
            "message": "request for test-user using test-password was rejected\nretry later",
        })
        with self.assertRaises(RecognizerError) as raised:
            TTShituRecognizer("test-user", "test-password", FakeSession(response)).recognize(self.image)
        message = str(raised.exception)
        self.assertIn("retry later", message)
        self.assertNotIn("test-user", message)
        self.assertNotIn("test-password", message)

    def test_rejects_invalid_json_and_response_shape(self):
        cases = (
            FakeResponse(json_error=ValueError("bad json")),
            FakeResponse([]),
            FakeResponse({"success": True, "data": "bad"}),
        )
        for response in cases:
            with self.subTest(body=response.body), self.assertRaises(RecognizerError):
                TTShituRecognizer("user", "secret", FakeSession(response)).recognize(self.image)


if __name__ == "__main__":
    unittest.main()
