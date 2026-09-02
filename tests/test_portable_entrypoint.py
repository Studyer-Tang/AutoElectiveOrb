import subprocess
import sys
import unittest
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]


class PortableEntrypointTests(unittest.TestCase):
    def test_main_bootstraps_engine_in_isolated_python(self):
        completed = subprocess.run(
            [sys.executable, "-I", str(PROJECT_ROOT / "engine" / "main.py"), "--help"],
            cwd=PROJECT_ROOT / "tests",
            capture_output=True,
            text=True,
            timeout=15,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        self.assertIn("AutoElective Orb local engine", completed.stdout)


if __name__ == "__main__":
    unittest.main()
