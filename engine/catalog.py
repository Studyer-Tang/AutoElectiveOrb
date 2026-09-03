#!/usr/bin/env python3
import argparse
import json
import os
import random
import sys
import time
from urllib.parse import urlsplit


ENGINE_DIR = os.path.dirname(os.path.abspath(__file__))
if ENGINE_DIR not in sys.path:
    sys.path.insert(0, ENGINE_DIR)


CURRENT_STAGE = "初始化"


def stage(name):
    global CURRENT_STAGE
    CURRENT_STAGE = name


def friendly_error(error):
    message = str(error).replace("\r", " ").replace("\n", " ")
    response = getattr(error, "response", None)
    known = {
        "NotInOperationTimeError": "当前学校阶段入口尚未开放，补退选与早期选课页面均不可读取。",
        "NotAgreedToSelectionAgreement": "尚未同意本学期选课协议。请先在浏览器进入选课系统并同意协议，然后重新读取。",
        "SessionExpiredError": "选课系统会话在读取前失效，请关闭其他选课页面后重新读取。",
        "InvalidTokenError": "统一认证令牌已失效，请重新读取。",
        "SharedSessionError": "学校检测到共享或冲突会话，请退出浏览器中的选课系统后重新读取。",
        "NoAuthInfoError": "辅修/双学位身份没有通过验证，请检查身份选项。",
        "CaughtCheatingError": "学校系统拒绝了自动请求，请停止重试并稍后再使用。",
    }
    explanation = known.get(error.__class__.__name__)
    if explanation:
        return "%s失败：%s" % (CURRENT_STAGE, explanation)
    if response is not None:
        parts = urlsplit(response.url)
        endpoint = "%s://%s%s" % (parts.scheme, parts.netloc, parts.path)
        if response.status_code == 200:
            return "%s失败：%s（%s）" % (CURRENT_STAGE, message, endpoint)
        return "%s失败：HTTP %s，%s（%s）" % (CURRENT_STAGE, response.status_code, message, endpoint)
    return "%s失败：%s" % (CURRENT_STAGE, message)


def course_dict(course, detailed=False):
    value = {
        "Name": course.name,
        "ClassNo": course.class_no,
        "School": course.school,
        "Teacher": course.teacher,
    }
    if detailed:
        try:
            value.update({
                "MaxQuota": course.max_quota,
                "UsedQuota": course.used_quota,
                "RemainingQuota": course.remaining_quota,
                "QuotaKnown": True,
            })
        except ValueError:
            value["QuotaKnown"] = False
    return value


def course_key(course):
    return course.name, course.class_no, course.school


def unique_courses(courses):
    result = []
    seen = set()
    for course in courses:
        key = course_key(course)
        if key not in seen:
            seen.add(key)
            result.append(course)
    return result


def merge_courses(*groups):
    """Merge course sources while preferring rows with quota/link/teacher detail."""
    merged = {}
    order = []
    for courses in groups:
        for course in courses:
            key = course_key(course)
            if key not in merged:
                merged[key] = course
                order.append(key)
                continue
            current = merged[key]
            current_score = int(current.status is not None) * 2 + int(bool(current.href)) * 2 + int(bool(current.teacher))
            candidate_score = int(course.status is not None) * 2 + int(bool(course.href)) * 2 + int(bool(course.teacher))
            if candidate_score > current_score:
                merged[key] = course
    return [merged[key] for key in order]


def run(config_path):
    from elective_orb_core.environ import Environ
    Environ().config_ini = config_path

    # These imports intentionally happen after selecting the generated config.
    from elective_orb_core.config import AutoElectiveConfig
    from elective_orb_core.const import USER_AGENT_LIST
    from elective_orb_core.elective import ElectiveClient
    from elective_orb_core.iaaa import IAAAClient
    from elective_orb_core.parser import (
        get_courses,
        get_courses_with_detail,
        get_elected_courses_with_drop,
        get_sida,
        get_table_header,
        get_tables,
        table_has_columns,
    )
    from elective_orb_core.exceptions import NotInOperationTimeError

    config = AutoElectiveConfig()
    username = config.iaaa_id
    password = config.iaaa_password
    user_agent = random.choice(USER_AGENT_LIST)

    iaaa = IAAAClient(timeout=config.iaaa_client_timeout)
    iaaa.set_user_agent(user_agent)
    stage("打开统一认证")
    iaaa.oauth_home()
    stage("统一认证登录")
    login = iaaa.oauth_login(username, password)
    token = login.json()["token"]

    elective = ElectiveClient(id="catalog", timeout=config.elective_client_timeout)
    elective.set_user_agent(user_agent)
    stage("进入选课系统")
    response = elective.sso_login(token)
    if config.is_dual_degree:
        stage("选择主修或辅双身份")
        response = elective.sso_login_dual_degree(get_sida(response), config.identity, response.url)

    basic_columns = ["课程名", "班号", "开课单位"]

    def course_tables(response):
        return [table for table in get_tables(response._tree) if table_has_columns(table, basic_columns)]

    def scan_supplement():
        elected_courses = []
        planned_courses = []

        # The plan page is the authoritative user-specific list and can contain
        # courses that are not visible on the current supplement page yet.
        stage("读取选课计划全部课程")
        try:
            planned_courses.extend(parse_basic_response(elective.get_PlanController()))
        except NotInOperationTimeError:
            pass

        seen_pages = set()
        for page in range(1, 101):
            stage("读取补退选课程第 %s 页" % page)
            response = elective.get_SupplyCancel(username) if page == 1 else elective.get_supplement(username, page=page)
            tables = course_tables(response)
            plan_tables = [table for table in tables if "补选" in get_table_header(table)]
            elected_tables = [table for table in tables if "退选" in get_table_header(table)]
            if not plan_tables:
                raise RuntimeError("补退选课程表结构不完整")
            page_plans = get_courses_with_detail(plan_tables[0])
            signature = tuple(course_key(item) for item in page_plans)
            if signature and signature in seen_pages:
                break
            if signature:
                seen_pages.add(signature)
            planned_courses.extend(page_plans)
            if not elected_courses and elected_tables:
                elected_courses, _ = get_elected_courses_with_drop(elected_tables[0])
            if len(page_plans) < 20:
                break
            time.sleep(0.5)
        return unique_courses(elected_courses), merge_courses(planned_courses)

    def parse_basic_response(response):
        courses = []
        for table in course_tables(response):
            courses.extend(get_courses(table))
        return unique_courses(courses)

    def scan_early_stage():
        elected_courses = []
        planned_courses = []

        stage("读取早期阶段已选结果")
        try:
            elected_courses = parse_basic_response(elective.get_ShowResults())
        except NotInOperationTimeError:
            pass

        stage("读取早期阶段选课计划")
        try:
            planned_courses.extend(parse_basic_response(elective.get_PlanController()))
        except NotInOperationTimeError:
            pass

        stage("进入早期选课页面")
        elective.get_WorkController()
        seen_pages = set()
        for page in range(1, 101):
            stage("读取早期课程第 %s 页" % page)
            response = elective.get_election(page=page)
            page_courses = []
            for table in course_tables(response):
                page_courses.extend(get_courses_with_detail(table, require_action=False))
            page_courses = unique_courses(page_courses)
            signature = tuple(course_key(item) for item in page_courses)
            if not signature or signature in seen_pages:
                break
            seen_pages.add(signature)
            planned_courses.extend(page_courses)
            if len(page_courses) < 20:
                break
            time.sleep(0.5)

        planned_courses = unique_courses(planned_courses)
        if not elected_courses and not planned_courses:
            raise RuntimeError("当前阶段页面中没有找到课程数据")
        return unique_courses(elected_courses), planned_courses

    try:
        elected, plans = scan_supplement()
        phase = "补退选阶段"
        can_execute_swap = True
    except NotInOperationTimeError:
        elected, plans = scan_early_stage()
        phase = "预选/早期阶段"
        can_execute_swap = False

    payload = {
        "Phase": phase,
        "CanExecuteSwap": can_execute_swap,
        "Elected": [course_dict(item) for item in elected],
        "Plans": [course_dict(item, detailed=True) for item in plans],
    }
    print("CATALOG_JSON=" + json.dumps(payload, ensure_ascii=False, separators=(",", ":")), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    options = parser.parse_args()
    try:
        run(options.config)
        return 0
    except Exception as error:
        print("CATALOG_ERROR=" + friendly_error(error), file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    sys.exit(main())
