import os
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "engine"))

from elective_orb_core.client import BaseClient  # noqa: E402


class SchoolClient(BaseClient):
    pass


class NetworkSafetyTests(unittest.TestCase):
    def test_school_client_ignores_stale_environment_proxy(self):
        client = SchoolClient(timeout=1)
        self.assertFalse(client._session.trust_env)
        client._session.close()


if __name__ == "__main__":
    unittest.main()
