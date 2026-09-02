#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: main.py
# modified: 2025-03-31

import os
import sys

# The embeddable Windows runtime uses an isolated module path. Always make the
# engine package beside this entry point importable, regardless of the caller's
# working directory or Python distribution.
ENGINE_DIR = os.path.dirname(os.path.abspath(__file__))
if ENGINE_DIR not in sys.path:
    sys.path.insert(0, ENGINE_DIR)

from elective_orb_core.cli import run

if __name__ == '__main__':
    sys.exit(run())
