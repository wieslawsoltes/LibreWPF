#!/usr/bin/env python3
"""Execute the SDK's native shim target against Windows and native host RIDs."""

import argparse
import ctypes
import hashlib
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


REPO_ROOT = Path(__file__).resolve().parents[2]
SDK_TARGETS = REPO_ROOT / "packaging/ProGPU.Wpf.Sdk/targets/ProGPU.Wpf.Sdk.targets"
DOTNET = shutil.which("dotnet")


class Win32CompatTargetTests(unittest.TestCase):
    def invoke(self, root, rid, compiler="cc"):
        output = root / "app"
        output.mkdir(exist_ok=True)
        project = root / "Consumer.proj"
        project.write_text(f"""<Project>
  <PropertyGroup>
    <ProGpuWpfUsePortableFrameworkReferences>true</ProGpuWpfUsePortableFrameworkReferences>
    <ProGpuWpfEnableWin32CompatShim>true</ProGpuWpfEnableWin32CompatShim>
    <ProGpuWpfEnablePortableBootstrap>false</ProGpuWpfEnablePortableBootstrap>
    <ProGpuWpfWin32CompatCompiler>{escape(compiler)}</ProGpuWpfWin32CompatCompiler>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Configuration>Release</Configuration>
    <RuntimeIdentifier>{rid}</RuntimeIdentifier>
    <TargetDir>{escape(str(output))}{os.sep}</TargetDir>
  </PropertyGroup>
  <Import Project="{escape(str(SDK_TARGETS))}" />
</Project>""", encoding="utf-8")
        result = subprocess.run(
            [DOTNET, "msbuild", str(project), "-nologo", "-nodeReuse:false",
             "-t:_ProGpuWpfSdkBuildWin32CompatShim"],
            cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=45)
        return result, output

    def test_windows_rids_do_not_invoke_host_compiler_or_add_shims(self):
        for rid in ("win-x64", "win-arm64", "win10-x64"):
            with self.subTest(rid=rid), tempfile.TemporaryDirectory(prefix="librewpf-win32-rid-") as directory:
                root = Path(directory)
                result, output = self.invoke(root, rid, str(root / "must-not-run-compiler"))
                self.assertEqual(0, result.returncode, result.stdout)
                self.assertEqual([], list(output.iterdir()))
                self.assertFalse((root / "obj").exists())

    def test_windows_target_preserves_caller_owned_native_file(self):
        with tempfile.TemporaryDirectory(prefix="librewpf-win32-owned-") as directory:
            root = Path(directory)
            (root / "app").mkdir()
            native_file = root / "app/dwmapi.dll"
            original = b"Caller-owned native asset; never replace or remove by basename."
            native_file.write_bytes(original)
            result, output = self.invoke(root, "win-arm64", str(root / "must-not-run-compiler"))
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertEqual(original, native_file.read_bytes())
            self.assertEqual([native_file], list(output.iterdir()))

    @unittest.skipUnless(platform.system() in ("Darwin", "Linux") and shutil.which("cc"),
                         "The compatibility shim compiles on macOS/Linux hosts with a C compiler.")
    def test_native_host_still_builds_and_copies_actual_compatibility_library(self):
        with tempfile.TemporaryDirectory(prefix="librewpf-win32-native-") as directory:
            root = Path(directory)
            result, output = self.invoke(root, "")
            self.assertEqual(0, result.returncode, result.stdout)
            files = sorted(output.iterdir())
            macos = platform.system() == "Darwin"
            self.assertEqual(8 if macos else 16, len(files))
            data = files[0].read_bytes()
            self.assertGreater(len(data), 4096)
            self.assertIn(data[:4], (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf") if macos else (b"\x7fELF",))
            digest = hashlib.sha256(data).digest()
            self.assertTrue(all(hashlib.sha256(file.read_bytes()).digest() == digest for file in files))
            self.assertTrue((output / ("dwmapi.dll" if macos else "dwmapi.dll.so")).exists())

            # Resolve the actual third-party P/Invoke entry point from the SDK's
            # built user32 payload, before any native window host is created.
            user32 = ctypes.CDLL(str(output / ("user32.dll" if macos else "user32.dll.so")))
            get_interval = user32.GetDoubleClickTime
            get_interval.argtypes = []
            get_interval.restype = ctypes.c_uint32
            actual = get_interval()
            self.assertGreaterEqual(actual, 1)
            self.assertLessEqual(actual, 5000)
            if macos:
                # Independent public AppKit query; do not change system settings
                # or create a window to manufacture a reference value.
                ctypes.CDLL("/System/Library/Frameworks/AppKit.framework/AppKit")
                objc = ctypes.CDLL("/usr/lib/libobjc.A.dylib")
                objc.objc_getClass.argtypes = [ctypes.c_char_p]
                objc.objc_getClass.restype = ctypes.c_void_p
                objc.sel_registerName.argtypes = [ctypes.c_char_p]
                objc.sel_registerName.restype = ctypes.c_void_p
                objc.objc_msgSend.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
                objc.objc_msgSend.restype = ctypes.c_double
                seconds = objc.objc_msgSend(objc.objc_getClass(b"NSEvent"),
                                           objc.sel_registerName(b"doubleClickInterval"))
                self.assertEqual(min(5000, round(seconds * 1000)) or 500, actual)
            else:
                self.assertEqual(500, actual)

    @unittest.skipUnless(platform.system() in ("Darwin", "Linux") and shutil.which("cc"),
                         "The native conversion control requires a C compiler.")
    def test_interval_conversion_retains_rounding_fallback_and_win32_bound(self):
        source = REPO_ROOT / "packaging/ProGPU.Wpf.Sdk/targets/ProGPU.Wpf.Sdk.Win32Compat.c"
        with tempfile.TemporaryDirectory(prefix="librewpf-click-interval-") as directory:
            root = Path(directory)
            control = root / "control.c"
            control.write_text('#include <assert.h>\n#include "' + str(source) + '"\n' + r'''
int main(void)
{
    assert(progpu_double_click_milliseconds(0.001) == 1);
    assert(progpu_double_click_milliseconds(0.0015) == 2);
    assert(progpu_double_click_milliseconds(0.0025) == 2);
    assert(progpu_double_click_milliseconds(0.0625) == 62);
    assert(progpu_double_click_milliseconds(0.1875) == 188);
    assert(progpu_double_click_milliseconds(5.001) == 5000);
    assert(progpu_double_click_milliseconds(0.0) == 500);
    assert(progpu_double_click_milliseconds(-1.0) == 500);
    assert(progpu_double_click_milliseconds(NAN) == 500);
    assert(progpu_double_click_milliseconds(INFINITY) == 500);
    return 0;
}
''', encoding="utf-8")
            executable = root / "control"
            result = subprocess.run(["cc", "-O2", str(control), "-o", str(executable)],
                                    capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            result = subprocess.run([str(executable)], capture_output=True, text=True, timeout=10)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--dotnet", default=DOTNET)
    arguments, remaining = parser.parse_known_args()
    DOTNET = arguments.dotnet
    if not DOTNET:
        raise SystemExit("A dotnet SDK is required to execute the real SDK target.")
    unittest.main(argv=[__file__, *remaining])
