#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: _internal.py
# modified: 2019-09-08

import gzip
import os


def mkdir(path):
    os.makedirs(path, exist_ok=True)

def absp(*paths):
    return os.path.normpath(os.path.abspath(os.path.join(os.path.dirname(__file__), *paths)))

def read_list(file, encoding='utf-8-sig', **kwargs):
    opener = gzip.open if file.endswith('.gz') else open
    with opener(file, 'rt', encoding=encoding, **kwargs) as fp:
        return [ line.rstrip('\n') for line in fp if not line.isspace() ]
