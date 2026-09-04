#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: parser.py
# modified: 2019-09-09

import re

from lxml import etree

from .course import Course
from .exceptions import UnexceptedHTMLFormat

_regexBzfxSida = re.compile(r'\?sida=(\S+?)&sttp=(?:bzx|bfx)')


def get_tree_from_response(r):
    return etree.HTML(r.text) # 不要用 r.content, 否则可能会以 latin-1 编码

def get_tree(content):
    return etree.HTML(content)

def get_tables(tree):
    if tree is None:
        return []
    return tree.xpath('.//table[contains(concat(" ", normalize-space(@class), " "), " datagrid ")]')

def get_table_header(table):
    rows = table.xpath('.//tr[contains(concat(" ", normalize-space(@class), " "), " datagrid-header ")]')
    rows = _own_rows(table, rows)
    if not rows:
        rows = table.xpath('.//tr[th][1]')
        rows = _own_rows(table, rows)
    if not rows:
        return []
    return [_normalized_text(cell) for cell in rows[0].xpath('./th | ./td')]

def get_table_trs(table):
    rows = table.xpath(
        './/tr[contains(concat(" ", normalize-space(@class), " "), " datagrid-odd ") '
        'or contains(concat(" ", normalize-space(@class), " "), " datagrid-even ")]'
    )
    rows = _own_rows(table, rows)
    if rows:
        return rows
    return _own_rows(table, table.xpath('.//tr[td]'))

def _own_rows(table, rows):
    """Exclude rows belonging to a datagrid nested inside this datagrid."""
    result = []
    for row in rows:
        ancestors = row.xpath('ancestor::table[1]')
        if ancestors and ancestors[0] == table:
            result.append(row)
    return result

def _normalized_text(node):
    return re.sub(r'\s+', '', ''.join(node.xpath('.//text()')))

def get_title(tree):
    title = tree.find('.//head/title')
    if title is None: # 双学位 sso_login 后先到 主修/辅双 选择页，这个页面没有 title 标签
        return None
    return title.text

def get_errInfo(tree):
    tds = tree.xpath(".//table//table//table//td")
    if len(tds) != 1:
        raise UnexceptedHTMLFormat(msg="Unable to locate the error message cell")
    td = tds[0]
    children = td.getchildren()
    if not children:
        raise UnexceptedHTMLFormat(msg="Error message cell is empty")
    strong = children[0]
    if strong.tag != 'strong' or strong.text not in ('出错提示:', '提示:'):
        raise UnexceptedHTMLFormat(msg="Unknown error message format")
    return "".join(td.xpath('./text()')).strip()

def get_tips(tree):
    tips = tree.xpath('.//td[@id="msgTips"]')
    if len(tips) == 0:
        return None
    cells = tips[0].xpath('.//table//table//td')
    if len(cells) < 2:
        raise UnexceptedHTMLFormat(msg="Unknown tips format")
    td = cells[1]
    return "".join(td.xpath('.//text()')).strip()

def get_sida(r):
    match = _regexBzfxSida.search(r.text)
    if match is None:
        raise UnexceptedHTMLFormat(msg="Unable to find dual-degree sida")
    return match.group(1)


def _column_indexes(header, names):
    missing = [name for name in names if name not in header]
    if missing:
        raise UnexceptedHTMLFormat(msg="Missing table columns: %s" % ", ".join(missing))
    return tuple(header.index(name) for name in names)


def _cell_text(cells, index):
    if index >= len(cells):
        raise UnexceptedHTMLFormat(msg="Course table row has too few cells")
    value = _normalized_text(cells[index])
    if not value:
        raise UnexceptedHTMLFormat(msg="Empty course table cell")
    return value

def _optional_cell_text(cells, index):
    return _normalized_text(cells[index]) if index is not None and index < len(cells) else ""

def _course_cells(row, indexes):
    cells = row.xpath('./th | ./td')
    if not cells or max(indexes) >= len(cells):
        return None
    try:
        values = tuple(_cell_text(cells, index) for index in indexes)
    except UnexceptedHTMLFormat:
        return None
    return cells, values

def table_has_columns(table, names):
    header = get_table_header(table)
    return all(name in header for name in names)

def get_courses(table):
    header = get_table_header(table)
    trs = get_table_trs(table)
    ixs = _column_indexes(header, ["课程名","班号","开课单位"])
    teacher_ix = header.index("教师") if "教师" in header else None
    cs = []
    for tr in trs:
        parsed = _course_cells(tr, ixs)
        if parsed is None:
            continue
        _, (name, class_no, school) = parsed
        teacher = _optional_cell_text(parsed[0], teacher_ix)
        c = Course(name, class_no, school, teacher=teacher)
        cs.append(c)
    return cs

def classify_lottery_outcome(value, allow_boolean=False):
    text = re.sub(r'\s+', '', value or '')
    if any(label in text for label in ('未选中', '未中签', '未抽中', '未录取', '落选', '失败')):
        return False
    if any(label in text for label in ('已选中', '已中签', '中签', '已抽中', '已录取', '录取成功')):
        return True
    if allow_boolean and text in ('是', '成功'):
        return True
    if allow_boolean and text in ('否', '不通过'):
        return False
    return None

def get_lottery_results(table):
    """Return (course, raw outcome, selected?) rows from the official result table."""
    header = get_table_header(table)
    rows = get_table_trs(table)
    indexes = _column_indexes(header, ["课程名", "班号", "开课单位"])
    teacher_ix = header.index("教师") if "教师" in header else None
    outcome_ix = next((index for index, name in enumerate(header)
                       if any(token in name for token in ("抽签结果", "选课结果", "中签", "选中", "状态"))), None)
    results = []
    for row in rows:
        parsed = _course_cells(row, indexes)
        if parsed is None:
            continue
        cells, (name, class_no, school) = parsed
        teacher = _optional_cell_text(cells, teacher_ix)
        outcome = _optional_cell_text(cells, outcome_ix)
        selected = classify_lottery_outcome(outcome, allow_boolean=outcome_ix is not None)
        if selected is None:
            for cell in cells:
                candidate = _normalized_text(cell)
                classified = classify_lottery_outcome(candidate)
                if classified is not None:
                    outcome, selected = candidate, classified
                    break
        results.append((Course(name, class_no, school, teacher=teacher), outcome or "未知", selected))
    return results

def get_courses_with_detail(table, require_action=True):
    header = get_table_header(table)
    trs = get_table_trs(table)
    base_ixs = _column_indexes(header, ["课程名","班号","开课单位"])
    quota_ix = header.index("限数/已选") if "限数/已选" in header else None
    teacher_ix = header.index("教师") if "教师" in header else None
    action_ix = next((header.index(name) for name in ("补选", "预选") if name in header), None)
    if require_action and action_ix is None:
        raise UnexceptedHTMLFormat(msg="Missing election action column")
    cs = []
    for tr in trs:
        parsed = _course_cells(tr, base_ixs)
        if parsed is None:
            continue
        t, (name, class_no, school) = parsed
        status = None
        try:
            if quota_ix is not None:
                quota_values = re.findall(r'\d+', _cell_text(t, quota_ix))
                if len(quota_values) >= 2:
                    status = (int(quota_values[0]), int(quota_values[1]))
            hrefs = t[action_ix].xpath('.//a/@href') if action_ix is not None and action_ix < len(t) else []
            href = hrefs[0] if hrefs else None
        except (ValueError, IndexError) as exc:
            raise UnexceptedHTMLFormat(msg="Invalid quota or election link") from exc
        if status is not None and (len(status) != 2 or min(status) < 0):
            raise UnexceptedHTMLFormat(msg="Invalid course quota values")
        teacher = _optional_cell_text(t, teacher_ix)
        c = Course(name, class_no, school, status, href, teacher)
        cs.append(c)
    return cs

def get_elected_courses_with_drop(table):
    """Parse elected courses table, also extracting drop hrefs if available.
    Returns: (courses_list, drop_map)
        courses_list: [Course, ...] - same as get_courses()
        drop_map: {Course: href} - mapping from course to its drop link
    """
    header = get_table_header(table)
    trs = get_table_trs(table)
    base_ixs = _column_indexes(header, ["课程名","班号","开课单位"])
    teacher_ix = header.index("教师") if "教师" in header else None

    # Try to find the drop column
    drop_ix = None
    for col_name in ["退选"]:
        if col_name in header:
            drop_ix = header.index(col_name)
            break

    cs = []
    drop_map = {}
    for tr in trs:
        parsed = _course_cells(tr, base_ixs)
        if parsed is None:
            continue
        t, (name, class_no, school) = parsed
        teacher = _optional_cell_text(t, teacher_ix)
        c = Course(name, class_no, school, teacher=teacher)
        cs.append(c)

        if drop_ix is not None:
            hrefs = t[drop_ix].xpath('.//a/@href')
            if hrefs:
                drop_map[c] = hrefs[0]

    return cs, drop_map
