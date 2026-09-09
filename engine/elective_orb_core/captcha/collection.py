"""Optional local copy of images already fetched; never performs network I/O."""
import base64
import hashlib
from io import BytesIO
import logging
import os
from pathlib import Path
import tempfile

from PIL import Image

_warned = False


def _atomic_write(path, data):
    if path.exists():
        return
    name = None
    try:
        with tempfile.NamedTemporaryFile(dir=str(path.parent), delete=False) as stream:
            name = stream.name
            stream.write(data)
        os.replace(name, path)
    finally:
        if name and os.path.exists(name):
            os.unlink(name)


def save_image_pair(target, raw, encoded, folders=('original', 'uploaded')):
    """Shared deduplicated, bounded storage for passive and batch collection."""
    with Image.open(BytesIO(raw)) as image:
        extension = {'PNG': 'png', 'JPEG': 'jpg', 'GIF': 'gif',
                     'BMP': 'bmp', 'WEBP': 'webp'}[image.format]
    uploaded = base64.b64decode(encoded, validate=True)
    digest = hashlib.sha256(raw).hexdigest()
    originals, previews = (Path(target) / name for name in folders)
    originals.mkdir(parents=True, exist_ok=True)
    previews.mkdir(parents=True, exist_ok=True)
    destination = originals / (digest + '.' + extension)
    if destination.exists():
        return False
    files = list(originals.iterdir()) + list(previews.iterdir())
    if len(files) + 2 > 10000 or sum(p.stat().st_size for p in files) + len(raw) + len(uploaded) > 500 * 1024 * 1024:
        raise OSError('collection limit reached')
    _atomic_write(previews / (digest + '.jpg'), uploaded)
    _atomic_write(destination, raw)
    return True


def collect_existing_image(raw, encoded):
    """Fail open: collection must not interrupt recognition or course operations."""
    global _warned
    try:
        directory = os.environ.get('AUTOELECTIVE_DATA_DIR')
        if not directory:
            return False
        root = Path(directory)
        if not (root / 'collect-captcha.enabled').is_file():
            return False
        save_image_pair(root / 'captcha-collection', raw, encoded)
        return True
    except Exception:
        if not _warned:
            logging.getLogger(__name__).warning('验证码本地保存失败或达到上限；识图流程继续。请检查收集目录。')
            _warned = True
        return False
