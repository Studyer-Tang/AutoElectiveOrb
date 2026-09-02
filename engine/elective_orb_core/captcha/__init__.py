#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: __init__.py
# modified: 2019-09-08

from .captcha import Captcha
from .local import LocalDdddOcrRecognizer

__all__ = ["Captcha", "LocalDdddOcrRecognizer"]
