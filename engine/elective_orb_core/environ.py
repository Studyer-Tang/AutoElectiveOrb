#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: environ.py
# modified: 2020-02-16

from collections import defaultdict
from threading import Event

from .utils import Singleton


class Environ(object, metaclass=Singleton):

    def __init__(self):
        self.config_ini = None
        self.iaaa_loop = 0
        self.elective_loop = 0
        self.errors = defaultdict(lambda: 0)
        self.iaaa_loop_thread = None
        self.elective_loop_thread = None
        self.goals = [] # [Course]
        self.ignored = {} # {Course, reason}
        self.stop_event = Event()
        self.worker_failures = {}
        self.captcha_recognizer = None
