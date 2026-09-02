#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: cli.py
# modified: 2020-02-20

from argparse import ArgumentParser
from contextlib import suppress
from datetime import datetime
from queue import Queue
from threading import Thread
import random

from . import __date__, __version__


def create_default_parser():

    parser = ArgumentParser(
        description='AutoElective Orb local engine v%s (%s)' % (__version__, __date__),
    )

    ## custom input files

    parser.add_argument(
        '-c',
        '--config',
        dest='config_ini',
        metavar="FILE",
        help='custom config file encoded with utf8',
    )

    ## boolean (flag) options

    parser.add_argument('--version', action='version', version=__version__)
    parser.add_argument(
        '--check', action='store_true',
        help='validate configuration, credentials and local dependencies without network requests',
    )
    parser.add_argument(
        '--start-at', metavar='HH:MM[:SS]',
        help='wait until the next occurrence of the specified local time before starting',
    )

    return parser


def setup_default_environ(options, environ):

    environ.config_ini = options.config_ini


def create_default_threads():

    # import here to ensure the singleton `config` will be init later than parse_args()
    from elective_orb_core.loop import run_elective_loop, run_iaaa_loop
    workers = [
        ("IAAA", run_iaaa_loop),
        ("Elective", run_elective_loop),
    ]

    return workers


def run():

    from .environ import Environ

    environ = Environ()

    parser = create_default_parser()
    options = parser.parse_args()
    setup_default_environ(options, environ)

    # Preflight before waiting or creating worker threads.
    from .captcha import LocalDdddOcrRecognizer
    from .config import AutoElectiveConfig
    config = AutoElectiveConfig()
    config.validate()
    _ = config.iaaa_password
    environ.local_recognizer = LocalDdddOcrRecognizer()
    print('LOCAL_OCR_READY', flush=True)
    from .swap_history import find_incomplete_transactions
    incomplete = find_incomplete_transactions()
    if incomplete:
        print('SWAP_RECOVERY_WARNING=%s' % ','.join(incomplete), flush=True)
    if options.check:
        print('Preflight check passed: configuration, credentials and OCR are ready.')
        return 0

    if options.start_at:
        parts = options.start_at.split(':')
        if len(parts) not in (2, 3):
            parser.error('--start-at must use HH:MM or HH:MM:SS')
        try:
            hour, minute = map(int, parts[:2])
            second = int(parts[2]) if len(parts) == 3 else 0
            now = datetime.now()
            target = now.replace(hour=hour, minute=minute, second=second, microsecond=0)
        except ValueError:
            parser.error('--start-at contains an invalid time')
        if target <= now:
            from datetime import timedelta
            target += timedelta(days=1)

        # Network preflight is intentionally read-only: authenticate, open the
        # help page, then log out. Course scanning and mutations start only
        # after the countdown reaches zero.
        from .const import USER_AGENT_LIST
        from .elective import ElectiveClient
        from .iaaa import IAAAClient
        from .parser import get_sida
        user_agent = random.choice(USER_AGENT_LIST)
        iaaa = IAAAClient(timeout=config.iaaa_client_timeout)
        iaaa.set_user_agent(user_agent)
        iaaa.oauth_home()
        login = iaaa.oauth_login(config.iaaa_id, config.iaaa_password)
        elective = ElectiveClient(id="preflight", timeout=config.elective_client_timeout)
        elective.set_user_agent(user_agent)
        response = elective.sso_login(login.json()["token"])
        if config.is_dual_degree:
            elective.sso_login_dual_degree(get_sida(response), config.identity, response.url)
        elective.get_HelpController()
        with suppress(Exception):
            elective.logout()
        print('PRESTART_LOGIN_READY', flush=True)
        print('SCHEDULE_WAITING=%s' % target.strftime('%Y-%m-%d %H:%M:%S'), flush=True)
        try:
            while not environ.stop_event.wait(min(30, max(0, (target - datetime.now()).total_seconds()))):
                if datetime.now() >= target:
                    break
        except KeyboardInterrupt:
            return 130
        print('SCHEDULE_STARTED', flush=True)

    workers = create_default_threads()

    outcomes = Queue()

    def supervise(name, target):
        try:
            target()
        except Exception as exc:
            outcomes.put((name, exc))
        else:
            outcomes.put((name, None))

    supervised = []
    for name, target in workers:
        thread = Thread(target=supervise, args=(name, target), name=name)
        thread.daemon = False
        supervised.append(thread)
        if thread.name == "IAAA":
            environ.iaaa_loop_thread = thread
        elif thread.name == "Elective":
            environ.elective_loop_thread = thread
        thread.start()

    exit_code = 0
    try:
        name, error = outcomes.get()
        if error is not None:
            environ.worker_failures[name] = repr(error)
            exit_code = 1
            print("[%s] worker failed: %s" % (name, error))
        elif not environ.stop_event.is_set():
            environ.worker_failures[name] = "worker exited unexpectedly"
            exit_code = 1
            print("[%s] worker exited unexpectedly" % name)
    except KeyboardInterrupt:
        print("Stopping...")
        exit_code = 130
    finally:
        environ.stop_event.set()
        for thread in supervised:
            thread.join(timeout=5)
    return exit_code
