import os
import sys
import unittest
from types import SimpleNamespace


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "engine"))

from elective_orb_core.parser import (  # noqa: E402
    get_courses,
    get_courses_with_detail,
    get_lottery_results,
    get_table_header,
    get_tables,
)
from elective_orb_core.parser import get_tree  # noqa: E402
from elective_orb_core.course import Course  # noqa: E402
from catalog import lottery_changes, lottery_payload, merge_courses, parse_result_courses  # noqa: E402


class ParserTests(unittest.TestCase):
    def test_plan_merge_keeps_all_courses_and_prefers_detailed_row(self):
        basic = Course("算法设计", 1, "信息科学技术学院", teacher="")
        other = Course("操作系统", 2, "信息科学技术学院", teacher="李老师")
        detailed = Course("算法设计", 1, "信息科学技术学院", status=(100, 96), href="/supplement/electSupplement.do?id=1", teacher="张老师")

        merged = merge_courses([basic, other], [detailed])

        self.assertEqual(len(merged), 2)
        self.assertEqual(merged[0].teacher, "张老师")
        self.assertEqual(merged[0].remaining_quota, 4)
        self.assertEqual(merged[1], other)

    def test_early_course_table_without_action_or_quota(self):
        tree = get_tree("""
        <html><body><table><tr><td>
          <table class="wide datagrid compact">
            <tr class="datagrid-header"><th>课程 名</th><th>班号</th><th>开课单位</th></tr>
            <tr class="datagrid-odd selected"><td>高等 数学</td><td>03</td><td>数学科学学院</td></tr>
          </table>
        </td></tr></table></body></html>
        """)
        tables = get_tables(tree)
        self.assertEqual(1, len(tables))
        self.assertEqual(["课程名", "班号", "开课单位"], get_table_header(tables[0]))
        courses = get_courses_with_detail(tables[0], require_action=False)
        self.assertEqual(("高等数学", 3, "数学科学学院"), (courses[0].name, courses[0].class_no, courses[0].school))
        self.assertIsNone(courses[0].status)

    def test_preselection_table_reads_quota_and_preselect_column(self):
        tree = get_tree("""
        <table class="datagrid">
          <tr class="datagrid-header"><th>课程名</th><th>班号</th><th>开课单位</th><th>教师</th><th>限数/已选</th><th>预选</th></tr>
          <tr class="datagrid-even"><td>程序设计</td><td>1</td><td>信息科学技术学院</td><td>张老师</td><td>100 / 87</td><td><a href="/elect">选择</a></td></tr>
        </table>
        """)
        course = get_courses_with_detail(get_tables(tree)[0])[0]
        self.assertEqual((100, 87), course.status)
        self.assertEqual("/elect", course.href)
        self.assertEqual("张老师", course.teacher)

    def test_single_results_table_is_enough(self):
        tree = get_tree("""
        <table class="datagrid">
          <tr><th>课程名</th><th>班号</th><th>开课单位</th></tr>
          <tr><td>大学英语</td><td>2</td><td>大学英语教研室</td></tr>
        </table>
        """)
        courses = get_courses(get_tables(tree)[0])
        self.assertEqual(1, len(courses))

    def test_official_result_probe_reads_only_course_tables(self):
        response = SimpleNamespace(_tree=get_tree("""
        <html><body>
          <table class="notice"><tr><td>抽签说明</td></tr></table>
          <table class="datagrid">
            <tr><th>课程名</th><th>班号</th><th>开课单位</th><th>抽签结果</th></tr>
            <tr><td>实变函数</td><td>1</td><td>数学科学学院</td><td>已选中</td></tr>
            <tr><td>泛函分析</td><td>2</td><td>数学科学学院</td><td>未选中</td></tr>
            <tr><td>常微分方程</td><td>3</td><td>数学科学学院</td><td>抽签中</td></tr>
          </table>
        </body></html>
        """))
        courses = parse_result_courses(response)
        self.assertEqual(3, len(courses))
        rows = get_lottery_results(get_tables(response._tree)[0])
        self.assertEqual([("已选中", True), ("未选中", False), ("抽签中", None)], [(outcome, selected) for _, outcome, selected in rows])
        payload = lottery_payload(rows)
        self.assertEqual((3, 1, 1, 1, 0), (
            payload["TotalCount"], payload["SelectedCount"], payload["NotSelectedCount"],
            payload["PendingCount"], payload["UnknownCount"]))
        before = {("实变函数", 1, "数学科学学院"): {"Outcome": "抽签中", "Name": "实变函数"}}
        after = {
            ("实变函数", 1, "数学科学学院"): {"Outcome": "已选中", "Name": "实变函数"},
            ("泛函分析", 2, "数学科学学院"): {"Outcome": "未选中", "Name": "泛函分析"},
        }
        changes = lottery_changes(before, after)
        self.assertEqual(["抽签中", "未出现"], [item["PreviousOutcome"] for item in changes])

    def test_pager_and_nested_rows_are_not_courses(self):
        tree = get_tree("""
        <table class="datagrid">
          <tr class="datagrid-header"><th>课程名</th><th>班号</th><th>开课单位</th><th>限数/已选</th></tr>
          <tr class="datagrid-odd"><td>线性代数</td><td>4</td><td>数学科学学院</td><td>80/79</td></tr>
          <tr class="datagrid-even"><td colspan="4">第 2 页 / 共 8 页</td></tr>
          <tr><td colspan="4"><table><tr class="datagrid-odd"><td>嵌套说明</td></tr></table></td></tr>
        </table>
        """)
        courses = get_courses_with_detail(get_tables(tree)[0], require_action=False)
        self.assertEqual(1, len(courses))
        self.assertEqual("线性代数", courses[0].name)
        self.assertEqual((80, 79), courses[0].status)


if __name__ == "__main__":
    unittest.main()
