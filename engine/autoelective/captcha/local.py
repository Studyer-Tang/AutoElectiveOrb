from io import BytesIO
from threading import Lock

from PIL import Image

from ..exceptions import RecognizerError
from .captcha import Captcha


class LocalDdddOcrRecognizer(object):
    """Process-wide local OCR instance; captcha images never leave the PC."""

    _instance = None
    _lock = Lock()

    def __init__(self):
        try:
            import ddddocr
        except ImportError as exc:
            raise ImportError("请先运行 install.cmd 安装本地 OCR 运行环境。") from exc
        with LocalDdddOcrRecognizer._lock:
            if LocalDdddOcrRecognizer._instance is None:
                LocalDdddOcrRecognizer._instance = ddddocr.DdddOcr(show_ad=False)
        self._ocr = LocalDdddOcrRecognizer._instance

    def recognize(self, raw):
        payload = raw
        if raw[:6] in (b"GIF87a", b"GIF89a"):
            image = Image.open(BytesIO(raw))
            image.seek(max(0, getattr(image, "n_frames", 1) - 1))
            buffer = BytesIO()
            image.convert("RGB").save(buffer, format="PNG")
            payload = buffer.getvalue()
        result = self._ocr.classification(payload)
        if not result or not result.isalnum() or len(result) != 4:
            raise RecognizerError(msg="ddddocr 本地识别结果格式无效")
        return Captcha(result)
