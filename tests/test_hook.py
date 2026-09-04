import os
import shutil
import sys
import tempfile
import unittest
from types import SimpleNamespace


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "engine"))

_TEMP_DIR = tempfile.mkdtemp(prefix="autoelective-hook-test-")
_CONFIG_PATH = os.path.join(_TEMP_DIR, "config.ini")
with open(_CONFIG_PATH, "w", encoding="utf-8") as handle:
    handle.write("[user]\nstudent_id = test-user\ndual_degree = false\nidentity = bzx\n")

from elective_orb_core.environ import Environ  # noqa: E402

Environ().config_ini = _CONFIG_PATH

from elective_orb_core.exceptions import ElectionFailedError  # noqa: E402
from elective_orb_core.hook import check_elective_tips  # noqa: E402
from elective_orb_core.parser import get_tree  # noqa: E402


def tearDownModule():
    shutil.rmtree(_TEMP_DIR, ignore_errors=True)


class ElectiveTipTests(unittest.TestCase):
    def test_maps_current_generic_supplement_failure_message(self):
        tree = get_tree("""
        <html><body><td id="msgTips"><table><tr><td><table><tr>
          <td>提示</td><td>补选课程失败。</td>
        </tr></table></td></tr></table></td></body></html>
        """)
        response = SimpleNamespace(_tree=tree, request=SimpleNamespace())
        with self.assertRaises(ElectionFailedError) as raised:
            check_elective_tips(response)
        self.assertIn("补选课程失败。", str(raised.exception))


if __name__ == "__main__":
    unittest.main()
