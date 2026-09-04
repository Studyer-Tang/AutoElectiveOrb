"""TTShitu captcha recognizer.

Only the captcha image and TTShitu credentials are sent to the provider.  The
school account, cookies, and request headers never enter this request.
"""

import base64
from io import BytesIO
import re

import requests
from PIL import Image, UnidentifiedImageError

from ..exceptions import RecognizerError
from ..secrets import get_ttshitu_credentials
from .captcha import Captcha


_CODE_PATTERN = re.compile(r"[A-Za-z0-9]{5}")


class TTShituRecognizer(object):
    ENDPOINT = "https://api.ttshitu.com/base64"
    TYPE_ID = 1003  # TTShitu: five mixed ASCII letters and digits
    MAX_IMAGE_BYTES = 3 * 1024 * 1024
    MAX_IMAGE_PIXELS = 4 * 1024 * 1024

    def __init__(self, username=None, password=None, session=None):
        if username is None or password is None:
            username, password = get_ttshitu_credentials()
        if not username or not password:
            raise RecognizerError(msg="TT 识图账号或密码为空")
        self._username = username
        self._password = password
        self._session = session or requests.Session()

    def recognize(self, raw):
        if not isinstance(raw, (bytes, bytearray)) or not raw:
            raise RecognizerError(msg="TT 识图收到的验证码图片为空或格式无效")
        if len(raw) > self.MAX_IMAGE_BYTES:
            raise RecognizerError(msg="验证码图片超过 TT 识图的 3 MB 限制")

        payload = {
            "username": self._username,
            "password": self._password,
            "typeid": self.TYPE_ID,
            "image": self._encode_image(raw),
        }
        try:
            response = self._session.post(
                self.ENDPOINT,
                json=payload,
                timeout=(5, 60),
            )
            response.raise_for_status()
            body = response.json()
        except requests.Timeout as exc:
            raise RecognizerError(msg="TT 识图请求超时，请检查网络后重试") from exc
        except requests.RequestException as exc:
            raise RecognizerError(msg="无法连接 TT 识图服务，请检查网络后重试") from exc
        except ValueError as exc:
            raise RecognizerError(msg="TT 识图返回了无法解析的数据") from exc

        if not isinstance(body, dict) or body.get("success") is not True:
            raise RecognizerError(msg=self._friendly_service_error(body))
        data = body.get("data")
        result = data.get("result") if isinstance(data, dict) else None
        code = re.sub(r"\s+", "", result) if isinstance(result, str) else ""
        if _CODE_PATTERN.fullmatch(code) is None:
            detail = "缺少结果字段" if not isinstance(result, str) else "实际长度 %d" % len(code)
            raise RecognizerError(
                msg="TT 识图返回格式不符（预期五位 ASCII 字母数字，%s）" % detail
            )
        return Captcha(code, engine="ttshitu")

    @classmethod
    def _encode_image(cls, raw):
        """Normalize static/animated captcha images for TTShitu's 1003 model."""
        try:
            with Image.open(BytesIO(raw)) as image:
                frame_count = getattr(image, "n_frames", 1)
                if frame_count > 1:
                    image.seek(frame_count - 1)
                width, height = image.size
                if width <= 0 or height <= 0 or width * height > cls.MAX_IMAGE_PIXELS:
                    raise RecognizerError(msg="验证码图片尺寸异常")

                # TTShitu's working reference implementation uploads the final
                # animation frame as JPEG.  A white canvas prevents transparent
                # pixels from becoming a black background during conversion.
                rgba = image.convert("RGBA")
                normalized = Image.new("RGB", rgba.size, "white")
                normalized.paste(rgba, mask=rgba.getchannel("A"))
                buffer = BytesIO()
                normalized.save(buffer, format="JPEG", quality=95, subsampling=0)
        except RecognizerError:
            raise
        except (UnidentifiedImageError, OSError, ValueError) as exc:
            raise RecognizerError(msg="验证码图片无法解析") from exc
        return base64.b64encode(buffer.getvalue()).decode("ascii")

    def _friendly_service_error(self, body):
        """Return a bounded, credential-redacted service error."""
        message = body.get("message", "") if isinstance(body, dict) else ""
        if not isinstance(message, str):
            message = ""
        if "用户名" in message or "密码" in message or "账号" in message:
            return "TT 识图账号或密码错误"
        if "余额" in message or "题分" in message:
            return "TT 识图余额不足"
        if "禁用" in message:
            return "TT 识图账号已被禁用"
        safe = re.sub(r"[\r\n\t]+", " ", message).strip()
        for secret in (self._username, self._password):
            if secret:
                safe = safe.replace(secret, "***")
        return "TT 识图服务拒绝请求：%s" % safe[:120] if safe else "TT 识图服务拒绝了本次请求"
