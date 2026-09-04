#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: loop.py
# modified: 2019-09-11

import json
import os
import random
import time
from collections import deque
from contextlib import suppress
from itertools import combinations
from queue import Empty, Full, Queue

from requests.exceptions import RequestException

from . import __date__, __version__
from ._internal import mkdir
from .captcha import LocalDdddOcrRecognizer
from .config import AutoElectiveConfig
from .const import USER_AGENT_LIST, WEB_LOG_DIR
from .elective import ElectiveClient
from .environ import Environ
from .exceptions import (
    CaughtCheatingError,
    CreditsLimitedError,
    ElectionFailedError,
    ElectionPermissionError,
    ElectionRepeatedError,
    ElectionSuccess,
    ElectiveException,
    ExamTimeConflictError,
    IAAAException,
    IAAAForbiddenError,
    IAAAIncorrectPasswordError,
    InvalidTokenError,
    MultiEnglishCourseError,
    MultiPECourseError,
    MutexCourseError,
    NoAuthInfoError,
    OperationFailedError,
    OperationTimeoutError,
    QuotaLimitedError,
    RecognizerError,
    ServerError,
    SessionExpiredError,
    SharedSessionError,
    StatusCodeError,
    SystemException,
    TimeConflictError,
    TipsException,
    UnexceptedHTMLFormat,
    UserInputException,
)
from .hook import _dump_request
from .iaaa import IAAAClient
from .logger import ConsoleLogger, FileLogger
from .parser import (
    get_courses,
    get_courses_with_detail,
    get_elected_courses_with_drop,
    get_sida,
    get_table_header,
    get_tables,
    table_has_columns,
)
from .swap_history import append_event as append_swap_event, start_transaction as start_swap_transaction

environ = Environ()
config = AutoElectiveConfig()
config.validate()
cout = ConsoleLogger("loop")
ferr = FileLogger("loop.error") # loop 的子日志，同步输出到 console

username = config.iaaa_id
password = config.iaaa_password
is_dual_degree = config.is_dual_degree
identity = config.identity
refresh_interval = config.refresh_interval
refresh_random_deviation = config.refresh_random_deviation
iaaa_client_timeout = config.iaaa_client_timeout
elective_client_timeout = config.elective_client_timeout
login_loop_interval = config.login_loop_interval
elective_client_pool_size = config.elective_client_pool_size
elective_client_max_life = config.elective_client_max_life
is_print_mutex_rules = config.is_print_mutex_rules

config.check_identify(identity)

_USER_WEB_LOG_DIR = os.path.join(WEB_LOG_DIR, config.get_user_subpath())
mkdir(_USER_WEB_LOG_DIR)

recognizer = environ.local_recognizer or LocalDdddOcrRecognizer()
RECOGNIZER_MAX_ATTEMPT = 5
AUTO_SCAN_PAGE_SIZE = 20
AUTO_SCAN_MAX_PAGES = 100
AUTO_SCAN_PAGE_INTERVAL = 0.5

electivePool = Queue(maxsize=elective_client_pool_size)
reloginPool = Queue(maxsize=elective_client_pool_size)

goals = environ.goals  # let N = len(goals);
ignored = environ.ignored
mutexes = []  # list[set[int]]; mutually-exclusive goal indexes
delays = []   # list[int]; per-goal quota thresholds

killedElective = ElectiveClient(-1)
NO_DELAY = -1
_target_page_cache = set()
_last_full_scan = 0.0


class _ElectiveNeedsLogin(Exception):
    pass

class _ElectiveExpired(Exception):
    pass


def _get_refresh_interval():
    if refresh_random_deviation <= 0:
        return refresh_interval
    # Safety invariant: jitter may slow a cycle down, but never make it faster
    # than the configured minimum interval.
    delta = random.random() * refresh_random_deviation * refresh_interval
    return refresh_interval + delta

def _ignore_course(course, reason):
    ignored[course.to_simplified()] = reason

def _add_error(e):
    clz = e.__class__
    name = clz.__name__
    key = "[%s] %s" % (e.code, name) if hasattr(clz, "code") else name
    environ.errors[key] += 1

def _format_timestamp(timestamp):
    if timestamp == -1:
        return str(timestamp)
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(timestamp))

def _dump_respose_content(content, filename):
    if os.environ.get("AUTOELECTIVE_ALLOW_SENSITIVE_DUMPS") != "1":
        cout.warning("Sensitive HTML dump skipped; set AUTOELECTIVE_ALLOW_SENSITIVE_DUMPS=1 to enable")
        return None
    path = os.path.join(_USER_WEB_LOG_DIR, filename)
    with open(path, 'wb') as fp:
        fp.write(content)


def _get_supply_tables(response):
    """Locate course tables by semantic headers, never by page position."""
    required = ["课程名", "班号", "开课单位"]
    tables = [table for table in get_tables(response._tree) if table_has_columns(table, required)]
    plan = next((table for table in tables if "补选" in get_table_header(table)), None)
    elected = next((table for table in tables if "退选" in get_table_header(table)), None)
    if plan is None or elected is None:
        raise UnexceptedHTMLFormat(response=response, msg="Unable to identify supply/cancel course tables")
    return plan, elected


def _validate_captcha(elective):
    """Validate one captcha with a bounded retry count."""
    for attempt in range(1, RECOGNIZER_MAX_ATTEMPT + 1):
        cout.info("Fetch a captcha (attempt %d/%d)" % (attempt, RECOGNIZER_MAX_ATTEMPT))
        r = elective.get_DrawServlet()
        try:
            captcha = recognizer.recognize(r.content)
        except RecognizerError as e:
            ferr.error(e)
            _add_error(e)
            environ.stop_event.wait(min(attempt, 3))
            continue
        cout.info("Recognition result: %s" % captcha.code)
        r = elective.get_Validate(username, captcha.code)
        try:
            result = r.json()["valid"]
        except (ValueError, KeyError) as e:
            raise OperationFailedError(msg="Unable to validate captcha: %s" % e) from e
        if result == "2":
            cout.info("Validation passed")
            return
        if result != "0":
            cout.warning("Unknown validation result: %s" % result)
        environ.stop_event.wait(min(attempt, 3))
    raise RecognizerError(msg="Captcha validation failed after %d attempts" % RECOGNIZER_MAX_ATTEMPT)


def _attempt_swap_rollback(elective, drop_course, transaction_id=None, target_course=None):
    """Best-effort rollback after a swap election failure.

    The remote service has no transaction API, so this can reduce but cannot
    eliminate the risk of losing the original course.
    """
    cout.critical("Swap did not complete; checking original course %s" % drop_course)
    if transaction_id:
        append_swap_event(transaction_id, "rollback_started", drop_course, target_course)
    try:
        # The original course is not necessarily one of the configured target
        # courses, so rollback must bypass the target-page cache.
        _, elected_courses, _, plans = _scan_all_supply_pages(elective, force_full=True)
        if drop_course in elected_courses:
            cout.info("Swap rollback not needed: original course is still elected")
            if transaction_id:
                append_swap_event(transaction_id, "rollback_not_needed", drop_course, target_course)
            return True
        original = next((course for course in plans if course == drop_course), None)
        if original is None or original.status is None or not original.is_available():
            cout.critical("Rollback unavailable: original course is not electable or has no quota")
            if transaction_id:
                append_swap_event(transaction_id, "rollback_failed", drop_course, target_course, "原课程不可选或无余量")
            return False
        _validate_captcha(elective)
        try:
            elective.get_ElectSupplement(original.href)
        except (ElectionSuccess, ElectionRepeatedError):
            cout.info("Swap rollback succeeded: %s" % drop_course)
            if transaction_id:
                append_swap_event(transaction_id, "rollback_success", drop_course, target_course)
            return True
        cout.critical("Swap rollback was not confirmed")
        if transaction_id:
            append_swap_event(transaction_id, "rollback_failed", drop_course, target_course, "学校页面未确认回滚成功")
    except Exception as e:
        ferr.exception(e)
        _add_error(e)
        cout.critical("Swap rollback failed: %s" % e)
        if transaction_id:
            append_swap_event(transaction_id, "rollback_failed", drop_course, target_course, e)
    return False


def _scan_all_supply_pages(elective, force_full=False):
    """Scan all pages initially, then refresh only target pages with periodic calibration."""
    global _last_full_scan
    elected = []
    drop_map = {}
    plans = []
    seen_pages = set()
    last_response = None
    cached_scan = bool(_target_page_cache) and not force_full and time.monotonic() - _last_full_scan < 300
    page_numbers = sorted({1}.union(_target_page_cache)) if cached_scan else range(1, AUTO_SCAN_MAX_PAGES + 1)
    detected_target_pages = set()

    for current_page in page_numbers:
        if current_page == 1:
            cout.info("Get SupplyCancel page 1 (automatic full scan)")
            response = elective.get_SupplyCancel(username)
            try:
                plan_table, elected_table = _get_supply_tables(response)
                page_elected, page_drop_map = get_elected_courses_with_drop(elected_table)
                page_plans = get_courses_with_detail(plan_table)
            except UnexceptedHTMLFormat:
                filename = "elective.get_SupplyCancel_%d.html" % int(time.time() * 1000)
                _dump_respose_content(response.content, filename)
                cout.info("Page dump requested: %s" % filename)
                raise
        else:
            for attempt in range(1, 4):
                cout.info("Get Supplement page %s (automatic full scan)" % current_page)
                response = elective.get_supplement(username, page=current_page)
                try:
                    plan_table, elected_table = _get_supply_tables(response)
                    page_elected, page_drop_map = get_elected_courses_with_drop(elected_table)
                    page_plans = get_courses_with_detail(plan_table)
                    break
                except UnexceptedHTMLFormat:
                    if attempt == 3:
                        raise OperationFailedError(msg="unable to get normal Supplement page %s" % current_page)
                    cout.warning("Empty page response; refresh SupplyCancel before retry %d/3" % (attempt + 1))
                    elective.get_SupplyCancel(username)

        signature = tuple((course.name, course.class_no, course.school) for course in page_plans)
        if signature and signature in seen_pages:
            cout.warning("Repeated page detected at page %s; automatic scan stopped safely" % current_page)
            break
        if signature:
            seen_pages.add(signature)

        if not elected:
            elected = page_elected
            drop_map = page_drop_map
        plans.extend(page_plans)
        if not cached_scan and any(candidate == goal for candidate in page_plans for goal in goals):
            detected_target_pages.add(current_page)
        last_response = response
        cout.info("Automatic scan page %s: %s courses" % (current_page, len(page_plans)))

        if not cached_scan and len(page_plans) < AUTO_SCAN_PAGE_SIZE:
            cout.info("Automatic full scan finished at page %s" % current_page)
            break
        if not cached_scan and current_page < AUTO_SCAN_MAX_PAGES:
            environ.stop_event.wait(AUTO_SCAN_PAGE_INTERVAL)
    else:
        if not cached_scan:
            raise OperationFailedError(msg="automatic page scan reached the safety limit of %s pages" % AUTO_SCAN_MAX_PAGES)

    if not cached_scan:
        _target_page_cache.clear()
        _target_page_cache.update(detected_target_pages)
        _last_full_scan = time.monotonic()
        cout.info("Target page cache updated: %s" % sorted(_target_page_cache))
    elif any(goal not in elected and goal not in plans and goal not in ignored for goal in goals):
        cout.warning("Target page cache became stale; running a full calibration scan")
        return _scan_all_supply_pages(elective, force_full=True)

    if last_response is None:
        raise OperationFailedError(msg="course page scan returned no response")

    return last_response, elected, drop_map, plans


def run_iaaa_loop():

    elective = None

    while not environ.stop_event.is_set():

        if elective is None:
            try:
                elective = reloginPool.get(timeout=1)
            except Empty:
                continue
            if elective is killedElective:
                cout.info("Quit IAAA loop")
                return

        environ.iaaa_loop += 1
        user_agent = random.choice(USER_AGENT_LIST)

        cout.info("Try to login IAAA (client: %s)" % elective.id)
        cout.info("User-Agent: %s" % user_agent)

        try:

            iaaa = IAAAClient(timeout=iaaa_client_timeout) # not reusable
            iaaa.set_user_agent(user_agent)

            # request elective's home page to get cookies
            r = iaaa.oauth_home()

            r = iaaa.oauth_login(username, password)

            try:
                token = r.json()["token"]
            except Exception as e:
                ferr.error(e)
                raise OperationFailedError(
                    msg="Unable to parse IAAA token (response body omitted)"
                ) from e

            elective.clear_cookies()
            elective.set_user_agent(user_agent)

            r = elective.sso_login(token)

            if is_dual_degree:
                sida = get_sida(r)
                sttp = identity
                referer = r.url
                r = elective.sso_login_dual_degree(sida, sttp, referer)

            if elective_client_max_life == -1:
                elective.set_expired_time(-1)
            else:
                elective.set_expired_time(int(time.time()) + elective_client_max_life)

            cout.info("Login success (client: %s, expired_time: %s)" % (
                      elective.id, _format_timestamp(elective.expired_time)))
            cout.info("")

            electivePool.put_nowait(elective)
            elective = None

        except (ServerError, StatusCodeError) as e:
            ferr.error(e)
            cout.warning("ServerError/StatusCodeError encountered")
            _add_error(e)

        except OperationFailedError as e:
            ferr.error(e)
            cout.warning("OperationFailedError encountered")
            _add_error(e)

        except RequestException as e:
            ferr.error(e)
            cout.warning("RequestException encountered")
            _add_error(e)

        except IAAAIncorrectPasswordError as e:
            cout.error(e)
            _add_error(e)
            raise e

        except IAAAForbiddenError as e:
            ferr.error(e)
            _add_error(e)
            raise e

        except IAAAException as e:
            ferr.error(e)
            cout.warning("IAAAException encountered")
            _add_error(e)

        except CaughtCheatingError as e:
            ferr.critical(e) # 严重错误
            _add_error(e)
            raise e

        except ElectiveException as e:
            ferr.error(e)
            cout.warning("ElectiveException encountered")
            _add_error(e)

        except json.JSONDecodeError as e:
            ferr.error(e)
            cout.warning("JSONDecodeError encountered")
            _add_error(e)

        except KeyboardInterrupt as e:
            raise e

        except Exception as e:
            ferr.exception(e)
            _add_error(e)
            raise e

        finally:
            t = login_loop_interval
            cout.info("")
            cout.info("IAAA login loop sleep %s s" % t)
            cout.info("")
            environ.stop_event.wait(t)


def run_elective_loop():

    elective = None
    noWait = False

    ## load courses

    cs = config.courses  # OrderedDict
    N = len(cs)
    cid_cix = {} # { cid: cix }

    for ix, (cid, c) in enumerate(cs.items()):
        goals.append(c)
        cid_cix[cid] = ix

    ## load mutex

    ms = config.mutexes
    mutexes.clear()
    mutexes.extend(set() for _ in range(N))

    for mid, m in ms.items():
        ixs = []
        for cid in m.cids:
            if cid not in cs:
                raise UserInputException("In 'mutex:%s', course %r is not defined" % (mid, cid))
            ix = cid_cix[cid]
            ixs.append(ix)
        for ix1, ix2 in combinations(ixs, 2):
            mutexes[ix1].add(ix2)
            mutexes[ix2].add(ix1)

    ## load swap

    ss = config.swaps  # { course_id: Course_to_drop }
    swap_map = {}  # { goal_ix: Course_to_drop }
    for cid, drop_course in ss.items():
        if cid in cid_cix:
            swap_map[cid_cix[cid]] = drop_course

    ## load delay

    ds = config.delays
    delays.clear()
    delays.extend([NO_DELAY] * N)

    for did, d in ds.items():
        cid = d.cid
        if cid not in cs:
            raise UserInputException("In 'delay:%s', course %r is not defined" % (did, cid))
        ix = cid_cix[cid]
        delays[ix] = d.threshold

    ## setup elective pool

    for ix in range(1, elective_client_pool_size + 1):
        client = ElectiveClient(id=ix, timeout=elective_client_timeout)
        client.set_user_agent(random.choice(USER_AGENT_LIST))
        electivePool.put_nowait(client)

    ## print header

    header = "# AutoElective Orb Engine v%s (%s) #" % (__version__, __date__)
    line = "#" + "-" * (len(header) - 2) + "#"

    cout.info(line)
    cout.info(header)
    cout.info(line)
    cout.info("")

    line = "-" * 30

    cout.info("> User Agent")
    cout.info(line)
    cout.info("pool_size: %d" % len(USER_AGENT_LIST))
    cout.info(line)
    cout.info("")
    cout.info("> Config")
    cout.info(line)
    cout.info("is_dual_degree: %s" % is_dual_degree)
    cout.info("identity: %s" % identity)
    cout.info("refresh_interval: %s" % refresh_interval)
    cout.info("refresh_random_deviation: %s" % refresh_random_deviation)
    cout.info("page_scan: automatic (up to %s pages)" % AUTO_SCAN_MAX_PAGES)
    cout.info("iaaa_client_timeout: %s" % iaaa_client_timeout)
    cout.info("elective_client_timeout: %s" % elective_client_timeout)
    cout.info("login_loop_interval: %s" % login_loop_interval)
    cout.info("elective_client_pool_size: %s" % elective_client_pool_size)
    cout.info("elective_client_max_life: %s" % elective_client_max_life)
    cout.info("is_print_mutex_rules: %s" % is_print_mutex_rules)
    cout.info(line)
    cout.info("")

    ## print swap rules

    if len(swap_map) > 0:
        cout.info("> Swap rules")
        cout.info(line)
        for ix, drop_c in swap_map.items():
            cout.info("%s => drop %s first" % (goals[ix], drop_c))
        cout.info(line)
        cout.info("")

    while not environ.stop_event.is_set():

        noWait = False

        if elective is None:
            try:
                elective = electivePool.get(timeout=1)
            except Empty:
                continue

        environ.elective_loop += 1
        cycle_started = time.monotonic()
        cycle_interval = _get_refresh_interval()

        cout.info("")
        cout.info("======== Loop %d ========" % environ.elective_loop)
        cout.info("")

        ## print current plans

        current = [ c for c in goals if c not in ignored ]
        if len(current) > 0:
            cout.info("> Current tasks")
            cout.info(line)
            for ix, course in enumerate(current):
                cout.info("%02d. %s" % (ix + 1, course))
            cout.info(line)
            cout.info("")

        ## print ignored course

        if len(ignored) > 0:
            cout.info("> Ignored tasks")
            cout.info(line)
            for ix, (course, reason) in enumerate(ignored.items()):
                cout.info("%02d. %s  %s" % (ix + 1, course, reason))
            cout.info(line)
            cout.info("")

        ## print mutex rules

        if any(mutexes):
            cout.info("> Mutex rules")
            cout.info(line)
            ixs = [(ix1, ix2) for ix1, related in enumerate(mutexes)
                   for ix2 in related if ix1 < ix2]
            if is_print_mutex_rules:
                for ix, (ix1, ix2) in enumerate(ixs):
                    cout.info("%02d. %s --x-- %s" % (ix + 1, goals[ix1], goals[ix2]))
            else:
                cout.info("%d mutex rules" % len(ixs))
            cout.info(line)
            cout.info("")

        ## print delay rules

        if any(delay != NO_DELAY for delay in delays):
            cout.info("> Delay rules")
            cout.info(line)
            ds = [ (cix, threshold) for cix, threshold in enumerate(delays) if threshold != NO_DELAY ]
            for ix, (cix, threshold) in enumerate(ds):
                cout.info("%02d. %s --- %d" % (ix + 1, goals[cix], threshold))
            cout.info(line)
            cout.info("")

        if len(current) == 0:
            cout.info("No tasks")
            cout.info("Quit elective loop")
            with suppress(Full):
                reloginPool.put_nowait(killedElective) # kill signal
            environ.stop_event.set()
            return

        ## print client info

        cout.info("> Current client: %s (qsize: %s)" % (elective.id, electivePool.qsize() + 1))
        cout.info("> Client expired time: %s" % _format_timestamp(elective.expired_time))
        cout.info("User-Agent: %s" % elective.user_agent)
        cout.info("")

        try:

            if not elective.has_logined:
                raise _ElectiveNeedsLogin  # quit this loop

            if elective.is_expired:
                try:
                    cout.info("Logout")
                    r = elective.logout()
                except Exception as e:
                    cout.warning("Logout error")
                    cout.exception(e)
                raise _ElectiveExpired   # quit this loop

            ## scan every supply/cancel page automatically

            page_r, elected, drop_map, plans = _scan_all_supply_pages(elective)

            ## check available courses

            cout.info("Get available courses")

            tasks = [] # [(ix, course, swap_drop_href)]
            for ix, c in enumerate(goals):
                if c in ignored:
                    continue
                elif c in elected:
                    cout.info("%s is elected, ignored" % c)
                    _ignore_course(c, "Elected")
                    for mix in mutexes[ix]:
                        mc = goals[mix]
                        if mc in ignored:
                            continue
                        cout.info("%s is simultaneously ignored by mutex rules" % mc)
                        _ignore_course(mc, "Mutex rules")
                else:
                    found = False
                    for c0 in plans: # c0 has detail
                        if c0 == c:
                            found = True
                            if c0.status is None:
                                cout.warning("Quota is unavailable for %s; skip this cycle safely" % c0)
                                break
                            if c0.is_available():
                                delay = delays[ix]
                                if delay != NO_DELAY and c0.remaining_quota > delay:
                                    cout.info("%s hasn't reached the delay threshold %d, skip" % (c0, delay))
                                else:
                                    # Check if this is a swap course
                                    swap_drop_href = None
                                    if ix in swap_map:
                                        drop_c = swap_map[ix]
                                        if drop_c in drop_map:
                                            swap_drop_href = drop_map[drop_c]
                                            cout.info("%s is AVAILABLE now ! (swap: will drop %s first)" % (c0, drop_c))
                                        else:
                                            cout.info("%s is AVAILABLE now ! (swap: %s not in elected, try direct)" % (c0, drop_c))
                                    else:
                                        cout.info("%s is AVAILABLE now !" % c0)
                                    tasks.append((ix, c0, swap_drop_href))
                            break
                    if not found:
                        raise UserInputException("%s is not in your course plan, please check your config." % c)

            tasks = deque([ (ix, c, dh) for ix, c, dh in tasks if c not in ignored ]) # filter again and change to deque

            ## elect available courses

            if len(tasks) == 0:
                cout.info("No course available")
                continue

            elected = []  # cache elected courses dynamically from `get_ElectSupplement`

            while len(tasks) > 0:

                ix, course, swap_drop_href = tasks.popleft()

                is_mutex = False

                # dynamically filter course by mutex rules
                for mix in mutexes[ix]:
                    mc = goals[mix]
                    if mc in elected: # ignore course in advanced
                        is_mutex = True
                        cout.info("%s --x-- %s" % (course, mc))
                        cout.info("%s is ignored by mutex rules in advance" % course)
                        _ignore_course(course, "Mutex rules")
                        break

                if is_mutex:
                    continue

                cout.info("Try to elect %s" % course)

                ## validate captcha first

                _validate_captcha(elective)

                ## if swap, drop old course first (after captcha is validated)

                swap_dropped = False
                swap_drop_course = swap_map.get(ix)
                swap_transaction = start_swap_transaction(swap_drop_course, course) if swap_drop_course is not None else None
                if swap_drop_href is not None:
                    cout.info("Swap: dropping old course before electing %s" % course)
                    try:
                        append_swap_event(swap_transaction, "drop_requested", swap_drop_course, course)
                        elective.drop_course(swap_drop_href)
                        swap_dropped = True
                        _, verified_elected, _, refreshed_plans = _scan_all_supply_pages(elective)
                        if swap_drop_course in verified_elected:
                            raise OperationFailedError(msg="Swap drop was not confirmed")
                        refreshed_target = next((item for item in refreshed_plans if item == course), None)
                        if (refreshed_target is None or refreshed_target.status is None
                                or not refreshed_target.is_available()):
                            cout.warning("Target quota disappeared immediately after drop")
                            _attempt_swap_rollback(elective, swap_drop_course, swap_transaction, course)
                            _ignore_course(course, "Swap aborted; target quota disappeared")
                            continue
                        course = refreshed_target
                        cout.info("Swap: drop confirmed")
                        append_swap_event(swap_transaction, "drop_confirmed", swap_drop_course, course)
                    except Exception as e:
                        ferr.error(e)
                        cout.warning("Swap: drop state is uncertain; checking the original course")
                        _add_error(e)
                        _attempt_swap_rollback(elective, swap_drop_course, swap_transaction, course)
                        _ignore_course(course, "Swap drop state uncertain; manual review required")
                        continue

                ## try to elect

                election_succeeded = False
                try:

                    if swap_transaction:
                        append_swap_event(swap_transaction, "target_requested", swap_drop_course, course)

                    r = elective.get_ElectSupplement(course.href)

                except ElectionRepeatedError as e:
                    ferr.error(e)
                    cout.warning("ElectionRepeatedError encountered")
                    _ignore_course(course, "Repeated")
                    _add_error(e)

                except TimeConflictError as e:
                    ferr.error(e)
                    cout.warning("TimeConflictError encountered")
                    _ignore_course(course, "Time conflict")
                    _add_error(e)

                except ExamTimeConflictError as e:
                    ferr.error(e)
                    cout.warning("ExamTimeConflictError encountered")
                    _ignore_course(course, "Exam time conflict")
                    _add_error(e)

                except ElectionPermissionError as e:
                    ferr.error(e)
                    cout.warning("ElectionPermissionError encountered")
                    _ignore_course(course, "Permission required")
                    _add_error(e)

                except CreditsLimitedError as e:
                    ferr.error(e)
                    cout.warning("CreditsLimitedError encountered")
                    _ignore_course(course, "Credits limited")
                    _add_error(e)

                except MutexCourseError as e:
                    ferr.error(e)
                    cout.warning("MutexCourseError encountered")
                    _ignore_course(course, "Mutual exclusive")
                    _add_error(e)

                except MultiEnglishCourseError as e:
                    ferr.error(e)
                    cout.warning("MultiEnglishCourseError encountered")
                    _ignore_course(course, "Multi English course")
                    _add_error(e)

                except MultiPECourseError as e:
                    ferr.error(e)
                    cout.warning("MultiPECourseError encountered")
                    _ignore_course(course, "Multi PE course")
                    _add_error(e)

                except ElectionFailedError as e:
                    ferr.error(e)
                    cout.warning("ElectionFailedError encountered") # 具体原因不明，且不能马上重试
                    _add_error(e)

                except QuotaLimitedError as e:
                    ferr.error(e)
                    # 选课网可能会发回异常数据，本身名额 180/180 的课会发 180/0，这个时候选课会得到这个错误
                    if course.status is not None and course.used_quota == 0:
                        cout.warning("Abnormal status of %s, a bug of 'elective.pku.edu.cn' found" % course)
                    else:
                        ferr.critical("Unexcepted behaviour") # 没有理由运行到这里
                        _add_error(e)

                except ElectionSuccess as e:
                    # 不从此处加入 ignored，而是在下回合根据教学网返回的实际选课结果来决定是否忽略
                    cout.info("%s is ELECTED !" % course)

                    # --------------------------------------------------------------------------
                    # Issue #25
                    # --------------------------------------------------------------------------
                    # 但是动态地更新 elected，如果同一回合内有多门课可以被选，并且根据 mutex rules，
                    # 低优先级的课和刚选上的高优先级课冲突，那么轮到低优先级的课提交选课请求的时候，
                    # 根据这个动态更新的 elected 它将会被提前地忽略（而不是留到下一循环回合的开始时才被忽略）
                    # --------------------------------------------------------------------------
                    r = e.response  # get response from error ... a bit ugly
                    _, elected_table = _get_supply_tables(r)
                    # use clear() + extend() instead of op `=` to ensure `id(elected)` doesn't change
                    elected.clear()
                    elected.extend(get_courses(elected_table))
                    election_succeeded = True
                    if swap_transaction:
                        append_swap_event(swap_transaction, "success", swap_drop_course, course)

                except RuntimeError as e:
                    ferr.critical(e)
                    ferr.critical("RuntimeError with Course(name=%r, class_no=%d, school=%r, status=%s, href=%r)" % (
                                    course.name, course.class_no, course.school, course.status, course.href))
                    # use this private function of 'hook.py' to dump the response from `get_SupplyCancel` or `get_supplement`
                    file = _dump_request(page_r)
                    ferr.critical("Dump response from 'get_SupplyCancel / get_supplement' to %s" % file)
                    raise e

                except Exception as e:
                    if swap_dropped:
                        append_swap_event(swap_transaction, "failed", swap_drop_course, course, e)
                        _attempt_swap_rollback(elective, swap_drop_course, swap_transaction, course)
                        _ignore_course(course, "Swap failed; manual review required")
                    raise e  # don't increase error count here

                if swap_transaction and not election_succeeded:
                    append_swap_event(swap_transaction, "failed", swap_drop_course, course, "目标课程未确认成功")
                    if swap_dropped:
                        _attempt_swap_rollback(elective, swap_drop_course, swap_transaction, course)
                        _ignore_course(course, "Swap failed; manual review required")

        except UserInputException as e:
            cout.error(e)
            _add_error(e)
            raise e

        except (ServerError, StatusCodeError) as e:
            ferr.error(e)
            cout.warning("ServerError/StatusCodeError encountered")
            _add_error(e)

        except OperationFailedError as e:
            ferr.error(e)
            cout.warning("OperationFailedError encountered")
            _add_error(e)

        except UnexceptedHTMLFormat as e:
            ferr.error(e)
            cout.warning("UnexceptedHTMLFormat encountered")
            _add_error(e)

        except RequestException as e:
            ferr.error(e)
            cout.warning("RequestException encountered")
            _add_error(e)

        except IAAAException as e:
            ferr.error(e)
            cout.warning("IAAAException encountered")
            _add_error(e)

        except _ElectiveNeedsLogin:
            cout.info("client: %s needs Login" % elective.id)
            reloginPool.put_nowait(elective)
            elective = None
            noWait = True

        except _ElectiveExpired:
            cout.info("client: %s expired" % elective.id)
            reloginPool.put_nowait(elective)
            elective = None
            noWait = True

        except (SessionExpiredError, InvalidTokenError, NoAuthInfoError, SharedSessionError) as e:
            ferr.error(e)
            _add_error(e)
            cout.info("client: %s needs relogin" % elective.id)
            reloginPool.put_nowait(elective)
            elective = None
            noWait = True

        except CaughtCheatingError as e:
            ferr.critical(e) # critical error !
            _add_error(e)
            raise e

        except SystemException as e:
            ferr.error(e)
            cout.warning("SystemException encountered")
            _add_error(e)

        except OperationTimeoutError as e:
            ferr.error(e)
            cout.warning("OperationTimeoutError encountered")
            _add_error(e)

        except TipsException as e:
            ferr.error(e)
            cout.warning("TipsException encountered")
            _add_error(e)

        except json.JSONDecodeError as e:
            ferr.error(e)
            cout.warning("JSONDecodeError encountered")
            _add_error(e)

        except KeyboardInterrupt as e:
            raise e

        except Exception as e:
            ferr.exception(e)
            _add_error(e)
            raise e

        finally:

            if elective is not None: # change elective client
                electivePool.put_nowait(elective)
                elective = None

            if noWait:
                cout.info("")
                cout.info("======== END Loop %d ========" % environ.elective_loop)
                cout.info("")
            else:
                elapsed = time.monotonic() - cycle_started
                t = max(0, cycle_interval - elapsed)
                cout.info("")
                cout.info("======== END Loop %d ========" % environ.elective_loop)
                cout.info("Main loop sleep %s s" % t)
                cout.info("")
                environ.stop_event.wait(t)
