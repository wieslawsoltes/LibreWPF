import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location('popup_diagnostic', Path(__file__).parents[1] / 'WindowsModalDesktop/diagnose-popup-scene.py')
diagnostic = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diagnostic)


class ChildIsolationTests(unittest.TestCase):
    def test_only_exact_portable_child_receives_hook_without_changing_original_arguments(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); app = root/'original.exe'; hook = root/'observer.dll'
            app.write_bytes(b'original'); hook.write_bytes(b'diagnostic')
            records = []
            kwargs = dict(cwd=tmp, stdout=object(), stderr=object(), creationflags=0, env={'ORIGINAL': 'kept'})
            original = mock.Mock(return_value=object())
            command = [str(app), '--evidence-directory', str(root/'app'), '--run-id', 'a'*32]
            with mock.patch.object(diagnostic.subprocess, 'Popen', original):
                with diagnostic.portable_hook(app, diagnostic.sha(app), hook, root, records):
                    diagnostic.subprocess.Popen(['microsoft.exe'], **kwargs)
                    diagnostic.subprocess.Popen(command, **kwargs)
                self.assertIs(original, diagnostic.subprocess.Popen)
            self.assertEqual(mock.call(['microsoft.exe'], **kwargs), original.call_args_list[0])
            call = original.call_args_list[1]
            self.assertEqual((command,), call.args)
            self.assertEqual({k: v for k, v in kwargs.items() if k != 'env'}, {k: v for k, v in call.kwargs.items() if k != 'env'})
            self.assertEqual('kept', call.kwargs['env']['ORIGINAL'])
            self.assertEqual(str(hook), call.kwargs['env']['DOTNET_STARTUP_HOOKS'])
            self.assertEqual({'ORIGINAL': 'kept'}, kwargs['env'])
            self.assertEqual(1, len(records))

    def test_duplicate_child_changed_bytes_and_existing_hook_fail_before_launch(self):
        for mode in ('duplicate', 'changed', 'hook'):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as tmp:
                root=Path(tmp); app=root/'original.exe'; hook=root/'observer.dll'
                app.write_bytes(b'original'); hook.write_bytes(b'observer'); expected=diagnostic.sha(app)
                records=[{}] if mode=='duplicate' else []
                if mode=='changed': app.write_bytes(b'changed')
                env={'DOTNET_STARTUP_HOOKS': 'foreign'} if mode=='hook' else {}
                with mock.patch.object(diagnostic.subprocess, 'Popen') as original:
                    with diagnostic.portable_hook(app, expected, hook, root, records):
                        with self.assertRaises(RuntimeError): diagnostic.subprocess.Popen([str(app)], env=env)
                    self.assertIs(original, diagnostic.subprocess.Popen)
                    original.assert_not_called()

    def test_original_launch_failure_and_global_callable_are_preserved(self):
        failure=OSError('original launch failed')
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); app=root/'original.exe'; hook=root/'observer.dll'
            app.write_bytes(b'original'); hook.write_bytes(b'observer')
            with mock.patch.object(diagnostic.subprocess, 'Popen', side_effect=failure) as original:
                with self.assertRaises(OSError) as seen:
                    with diagnostic.portable_hook(app, diagnostic.sha(app), hook, root, []):
                        diagnostic.subprocess.Popen([str(app)], env={})
                self.assertIs(failure, seen.exception)
                self.assertIs(original, diagnostic.subprocess.Popen)
                original.assert_called_once()


if __name__ == '__main__': unittest.main()
