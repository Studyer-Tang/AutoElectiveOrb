#!/usr/bin/env python3
import json
import os
import threading
import time
import uuid

from .const import DATA_DIR


_lock = threading.Lock()
JSON_PATH = os.path.join(DATA_DIR, "swap-history.jsonl")
TEXT_PATH = os.path.join(DATA_DIR, "swap-history.log")

_LABELS = {
    "prepared": "已准备",
    "drop_requested": "已提交退课",
    "drop_confirmed": "已确认退课",
    "target_requested": "已提交目标课程",
    "success": "换课成功",
    "failed": "换课失败",
    "rollback_started": "开始回滚",
    "rollback_not_needed": "无需回滚",
    "rollback_success": "回滚成功",
    "rollback_failed": "回滚失败",
}


def _course_value(course):
    if course is None:
        return None
    return {"name": course.name, "class_no": course.class_no, "school": course.school}


def _display(course):
    if course is None:
        return "-"
    return "%s（%s班，%s）" % (course.name, course.class_no, course.school)


def start_transaction(drop_course, target_course):
    transaction_id = "%s-%s" % (time.strftime("%Y%m%d%H%M%S"), uuid.uuid4().hex[:8])
    append_event(transaction_id, "prepared", drop_course, target_course)
    return transaction_id


def append_event(transaction_id, status, drop_course=None, target_course=None, message=""):
    os.makedirs(DATA_DIR, exist_ok=True)
    payload = {
        "time": time.strftime("%Y-%m-%d %H:%M:%S"),
        "transaction_id": transaction_id,
        "status": status,
        "drop": _course_value(drop_course),
        "target": _course_value(target_course),
        "message": str(message),
    }
    safe_message = str(message).replace("\r", " ").replace("\n", " ").replace("\t", " ")
    line = "{time}\t{label}\t编号 {tx}\t原课程 {drop}\t目标 {target}".format(
        time=payload["time"], label=_LABELS.get(status, status), tx=transaction_id,
        drop=_display(drop_course), target=_display(target_course))
    if safe_message:
        line += "\t" + safe_message
    with _lock:
        with open(JSON_PATH, "a", encoding="utf-8") as handle:
            handle.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        with open(TEXT_PATH, "a", encoding="utf-8") as handle:
            handle.write(line + "\n")
            handle.flush()
            os.fsync(handle.fileno())


def find_incomplete_transactions():
    if not os.path.exists(JSON_PATH):
        return []
    latest = {}
    try:
        with open(JSON_PATH, "r", encoding="utf-8") as handle:
            for line in handle:
                try:
                    item = json.loads(line)
                    latest[item["transaction_id"]] = item["status"]
                except (ValueError, KeyError, TypeError):
                    continue
    except OSError:
        return []
    uncertain = {"drop_requested", "drop_confirmed", "target_requested", "rollback_started", "rollback_failed"}
    return sorted(transaction_id for transaction_id, status in latest.items() if status in uncertain)
