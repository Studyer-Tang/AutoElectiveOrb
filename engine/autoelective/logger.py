#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: logger.py
# modified: 2019-09-09

import logging
import os
from logging.handlers import TimedRotatingFileHandler

from ._internal import mkdir
from .config import AutoElectiveConfig
from .const import ERROR_LOG_DIR

config = AutoElectiveConfig()

_USER_ERROR_LOG_DIR = os.path.join(ERROR_LOG_DIR, config.get_user_subpath())
mkdir(_USER_ERROR_LOG_DIR)


class BaseLogger(object):

    default_level = logging.DEBUG
    default_format = logging.Formatter("[%(levelname)s] %(name)s, %(asctime)s, %(message)s", "%H:%M:%S")

    def __init__(self, name, level=None, format=None):
        if self.__class__ is __class__:
            raise NotImplementedError
        self._name = name
        self._level = level if level is not None else self.__class__.default_level
        self._format = format if format is not None else self.__class__.default_format
        self._logger = logging.getLogger(self._name)
        self._logger.setLevel(self._level)
        handler_key = "%s:%s" % (self.__class__.__name__, self._name)
        if not any(getattr(handler, "_autoelective_key", None) == handler_key
                   for handler in self._logger.handlers):
            handler = self._get_handler()
            handler._autoelective_key = handler_key
            self._logger.addHandler(handler)
        self._logger.propagate = False

    def _get_handler(self):
        raise NotImplementedError

    def debug(self, msg, *args, **kwargs):
        return self._logger.debug(msg, *args, **kwargs)

    def info(self, msg, *args, **kwargs):
        return self._logger.info(msg, *args, **kwargs)

    def warning(self, msg, *args, **kwargs):
        return self._logger.warning(msg, *args, **kwargs)

    def error(self, msg, *args, **kwargs):
        return self._logger.error(msg, *args, **kwargs)

    def exception(self, msg, *args, **kwargs):
        kwargs.setdefault("exc_info", True)
        return self._logger.exception(msg, *args, **kwargs)

    def critical(self, msg, *args, **kwargs):
        return self._logger.critical(msg, *args, **kwargs)


class ConsoleLogger(BaseLogger):
    """ 控制台日志输出类 """

    default_level = logging.DEBUG

    def _get_handler(self):
        handler = logging.StreamHandler()
        handler.setLevel(self._level)
        handler.setFormatter(self._format)
        return handler


class FileLogger(BaseLogger):
    """ 文件日志输出类 """

    default_level = logging.WARNING

    def _get_handler(self):
        file = os.path.join(_USER_ERROR_LOG_DIR, "%s.log" % self._name)
        handler = TimedRotatingFileHandler(
            file, when='d', interval=1, backupCount=14, encoding="utf-8-sig"
        )
        handler.setLevel(self._level)
        handler.setFormatter(self._format)
        return handler
