import os
import sys
import tempfile
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "engine"))

from elective_orb_core.course import Course  # noqa: E402
from elective_orb_core import swap_history  # noqa: E402


class SwapHistoryTests(unittest.TestCase):
    def test_records_human_and_machine_history_and_detects_incomplete(self):
        with tempfile.TemporaryDirectory() as directory:
            old_json, old_text = swap_history.JSON_PATH, swap_history.TEXT_PATH
            try:
                swap_history.JSON_PATH = os.path.join(directory, "history.jsonl")
                swap_history.TEXT_PATH = os.path.join(directory, "history.log")
                drop = Course("旧课程", 1, "学院")
                target = Course("新课程", 2, "学院")
                transaction = swap_history.start_transaction(drop, target)
                swap_history.append_event(transaction, "drop_confirmed", drop, target)
                self.assertEqual([transaction], swap_history.find_incomplete_transactions())
                swap_history.append_event(transaction, "rollback_success", drop, target)
                self.assertEqual([], swap_history.find_incomplete_transactions())
                uncertain = swap_history.start_transaction(drop, target)
                swap_history.append_event(uncertain, "manual_review", drop, target, "无法确认最终课表")
                self.assertEqual([uncertain], swap_history.find_incomplete_transactions())
                with open(swap_history.TEXT_PATH, encoding="utf-8") as handle:
                    text = handle.read()
                self.assertIn("已确认退课", text)
                self.assertIn("回滚成功", text)
                self.assertIn("状态待人工核对", text)
            finally:
                swap_history.JSON_PATH, swap_history.TEXT_PATH = old_json, old_text


if __name__ == "__main__":
    unittest.main()
