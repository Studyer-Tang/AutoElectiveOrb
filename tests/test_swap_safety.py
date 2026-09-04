import os
import subprocess
import sys
import tempfile
import textwrap
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


class SwapSafetyTests(unittest.TestCase):
    def test_rollback_uses_refreshed_course_list_as_postcondition(self):
        with tempfile.TemporaryDirectory() as directory:
            config = os.path.join(directory, "config.ini")
            with open(config, "w", encoding="utf-8") as handle:
                handle.write(textwrap.dedent("""
                    [user]
                    student_id = test-user
                    dual_degree = false
                    identity = bzx
                    [client]
                    refresh_interval = 6
                    random_deviation = 0.15
                    iaaa_client_timeout = 20
                    elective_client_timeout = 35
                    elective_client_pool_size = 1
                    elective_client_max_life = 600
                    login_loop_interval = 4
                    print_mutex_rules = false
                    debug_print_request = false
                    debug_dump_request = false
                    [captcha]
                    provider = ttshitu
                    [safety]
                    enable_unsafe_auto_swap = false
                    [course:1]
                    name = Target
                    class = 1
                    school = School
                """))

            script = textwrap.dedent("""
                import sys
                from elective_orb_core.environ import Environ
                Environ().config_ini = sys.argv[1]
                import elective_orb_core.loop as loop
                from elective_orb_core.course import Course

                class FakeElective:
                    def __init__(self): self.calls = 0
                    def get_ElectSupplement(self, href): self.calls += 1

                drop = Course('Original', 1, 'School')
                target = Course('Target', 1, 'School')
                events = []
                loop.append_swap_event = lambda *args, **kwargs: events.append(args[1])
                loop._validate_captcha = lambda elective: None

                # Refresh both the target link and captcha authorization after
                # the original course is dropped.
                calls = []
                fresh_target = Course('Target', 1, 'School', status=(10, 2), href='/supplement/electSupplement.do?id=fresh')
                loop._scan_all_supply_pages = lambda elective, force_full=False: (
                    calls.append(('refresh', force_full)) or (None, [], {}, [fresh_target]))
                loop._validate_captcha = lambda elective: calls.append(('captcha', None))
                prepared = loop._prepare_swap_target_submission(FakeElective(), drop, target)
                assert prepared.href.endswith('fresh')
                assert calls == [('refresh', True), ('captcha', None)]

                # A delayed school response must not trigger rollback if the
                # authoritative refreshed list already contains the target.
                fake = FakeElective()
                loop._read_swap_snapshot = lambda elective, purpose: ([target], [])
                loop._validate_captcha = lambda elective: None
                result = loop._attempt_swap_rollback(fake, drop, 'tx-1', target)
                assert result == 'target_confirmed'
                assert fake.calls == 0
                assert events[-1] == 'success'

                # A reported rollback is only successful after the refreshed
                # list contains the original course.
                available_drop = Course('Original', 1, 'School', status=(10, 1), href='/supplement/electSupplement.do?id=1')
                snapshots = iter([([], [available_drop]), ([drop], [])])
                loop._read_swap_snapshot = lambda elective, purpose: next(snapshots)
                events[:] = []
                fake = FakeElective()
                result = loop._attempt_swap_rollback(fake, drop, 'tx-2', target)
                assert result == 'original_confirmed'
                assert fake.calls == 1
                assert events[-1] == 'rollback_success'
            """
            )
            environment = os.environ.copy()
            environment.update({
                "PYTHONPATH": os.path.join(ROOT, "engine"),
                "AUTOELECTIVE_DATA_DIR": directory,
                "AUTOELECTIVE_IAAA_PASSWORD": "test-only",
                "AUTOELECTIVE_TT_USERNAME": "test-only",
                "AUTOELECTIVE_TT_PASSWORD": "test-only",
            })
            result = subprocess.run(
                [sys.executable, "-c", script, config],
                cwd=ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=20,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
