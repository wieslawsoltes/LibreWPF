#!/usr/bin/env python3
"""Check actual source-app runtime selection without executing the native files."""
import argparse
import hashlib
import json
from itertools import product
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--progpu-root', type=Path, default=ROOT / 'external/ProGPU')
    parser.add_argument('--runtime-root', required=True, type=Path)
    args = parser.parse_args()
    producer = args.progpu_root.resolve(strict=True)
    payload = args.runtime_root.resolve(strict=True)
    files = {}
    for rid in ('linux-x64', 'linux-arm64'):
        native = payload / rid / 'native'
        receipt = json.loads((native / 'wgpu-native-linux.json').read_text())
        assert receipt['rid'] == rid and receipt['abiTestsPassed'] is True
        for name, expected in receipt['files'].items():
            assert hashlib.sha256((native / name).read_bytes()).hexdigest() == expected
        for path in native.iterdir():
            assert path.is_file() and not path.is_symlink()
            files['runtimes/' + path.relative_to(payload).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    count = 0
    with tempfile.TemporaryDirectory(prefix='wpf-xlib-source-assets-') as temporary:
        # Evaluate discovery with the original target and payload in a private
        # source layout. This does not build or execute a native dependency.
        source = Path(temporary) / 'source'
        target = source / 'src/ProGPU.Backend/buildTransitive/ProGPU.Backend.targets'
        target.parent.mkdir(parents=True)
        target.write_bytes((producer / target.relative_to(source)).read_bytes())
        windows_target = producer / target.relative_to(source).parent / "ProGPU.Backend.Windows.targets"
        if windows_target.exists():
            (target.parent / windows_target.name).write_bytes(windows_target.read_bytes())
        stage = source / 'artifacts/progpu-xlib/package'
        stage.parent.mkdir(parents=True)
        stage.symlink_to(payload, target_is_directory=True)
        for boundary, suffix, explicit in product(
                ('ProGPU.Wpf.MultiWindowSmokeHarness', 'ProGPU.Wpf.Tests'), ('/', ''), (False, True)):
            for rid, output, test in (('', 'Exe', False), ('linux-x64', 'Exe', False),
                                      ('linux-arm64', 'Exe', False), ('win-x64', 'Exe', False),
                                      ('osx-arm64', 'Exe', False), ('', 'Library', False),
                                      ('', 'Library', True)):
                document = ET.Element('Project')
                group = ET.SubElement(document, 'PropertyGroup')
                values = dict(OutputType=output, IsTestProject=str(test).lower(), RuntimeIdentifier=rid,
                              ProGpuSourceRoot=str(source) + suffix)
                if explicit:
                    values['ProGpuXlibSourceRuntimeRoot'] = str(payload)
                for name, value in values.items():
                    ET.SubElement(group, name).text = value
                ET.SubElement(document, 'Import', Project=str(ROOT / 'src' / boundary / 'Directory.Build.targets'))
                ET.SubElement(document, 'Import', Project=str(ROOT / 'eng/LibreWPF.XlibRuntime.targets'))
                project = Path(temporary) / 'control.proj'
                ET.ElementTree(document).write(project, encoding='unicode')
                result = subprocess.run([args.dotnet, 'msbuild', str(project), '-nologo', '-getItem:Content',
                                         '-p:ProGpuSourceRoot=' + str(source) + suffix],
                                        capture_output=True, text=True, timeout=30)
                assert result.returncode == 0, result.stdout + result.stderr
                items = json.loads(result.stdout)['Items']['Content']
                expected = {name: digest for name, digest in files.items()
                            if (output == 'Exe' or test) and (not rid or name.startswith('runtimes/' + rid + '/'))}
                actual = {}
                for item in items:
                    assert item['Link'] not in actual, 'Duplicate runtime asset'
                    assert item['CopyToOutputDirectory'] == item['CopyToPublishDirectory'] == 'PreserveNewest'
                    actual[item['Link']] = hashlib.sha256(Path(item['FullPath']).read_bytes()).hexdigest()
                assert actual == expected, (boundary, suffix, explicit, rid, output, test, actual, expected)
                count += 1
    print(f'{count} real-payload source asset evaluations passed; no native application executed.')


if __name__ == '__main__':
    main()
