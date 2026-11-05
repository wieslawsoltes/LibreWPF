#!/usr/bin/env python3
"""Verify real staged native assets at the source test/application boundary."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--progpu-root', type=Path, default=ROOT / 'external/ProGPU')
    args = parser.parse_args()
    producer = args.progpu_root.resolve(strict=True)
    payload = producer / 'artifacts/progpu-native/package'
    files = {path.relative_to(payload).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
             for path in payload.rglob('*') if path.is_file() and 'sdk' not in path.relative_to(payload).parts}
    assert files and all(name.startswith('runtimes/') for name in files)
    assert any(name.endswith('/libprogpu_native.dylib') for name in files)
    assert any(name.endswith('/progpu_native.dll') for name in files)
    assert any(name.endswith('/libprogpu_native.so') for name in files)
    count = 0
    with tempfile.TemporaryDirectory(prefix='wpf-native-source-assets-') as temporary:
        for suffix in ('', '/'):
            for explicit in (False, True):
                for rid, output, test, enabled in (
                    ('', 'Library', True, True), ('osx-arm64', 'Exe', False, True),
                    ('win-arm64', 'WinExe', False, True), ('linux-x64', 'Exe', False, True),
                    ('', 'Library', False, True), ('', 'Exe', False, False),
                    ('not-a-runtime', 'Exe', False, True), ('', 'Exe', False, True)):
                    document = ET.Element('Project')
                    group = ET.SubElement(document, 'PropertyGroup')
                    values = dict(OutputType=output, IsTestProject=str(test).lower(), RuntimeIdentifier=rid,
                                  LibreWpfUseSourceNativeRuntime=str(enabled).lower(),
                                  ProGpuSourceRoot=str(producer) + suffix)
                    if explicit:
                        # An unrelated source path must not replace an explicit runtime root.
                        values['ProGpuSourceRoot'] = temporary + '/unavailable/'
                        values['ProGpuNativeRuntimeRoot'] = str(payload) + suffix
                    for name, value in values.items():
                        ET.SubElement(group, name).text = value
                    for _ in range(2):
                        ET.SubElement(document, 'Import', Project=str(ROOT / 'eng/LibreWPF.NativeRuntime.targets'))
                    project = Path(temporary) / 'control.proj'
                    ET.ElementTree(document).write(project, encoding='unicode')
                    result = subprocess.run([args.dotnet, 'msbuild', str(project), '-nologo', '-verbosity:quiet',
                                             '-getItem:Content', '-p:ProGpuSourceRoot=' + values['ProGpuSourceRoot']],
                                            capture_output=True, text=True, timeout=30)
                    assert result.returncode == 0, result.stdout + result.stderr
                    items = json.loads(result.stdout)['Items']['Content']
                    expected = {name if not rid else name.removeprefix('runtimes/' + rid + '/native/'): digest
                                for name, digest in files.items()
                                if enabled and (output in ('Exe', 'WinExe') or test)
                                and (not rid or name.startswith('runtimes/' + rid + '/native/'))}
                    actual = {}
                    for item in items:
                        assert item['Link'] not in actual, 'Duplicate native asset'
                        assert item['CopyToOutputDirectory'] == item['CopyToPublishDirectory'] == 'PreserveNewest'
                        actual[item['Link']] = hashlib.sha256(Path(item['FullPath']).read_bytes()).hexdigest()
                    assert actual == expected, (suffix, explicit, rid, output, test, enabled)
                    count += 1
    print(f'{count} real-payload native source asset evaluations passed; no application executed.')


if __name__ == '__main__':
    main()
