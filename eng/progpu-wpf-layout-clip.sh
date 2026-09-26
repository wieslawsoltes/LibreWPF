#!/usr/bin/env bash
set -euo pipefail

# This additive source-contract gate does not qualify native rendering or idle
# application performance. The full SDK/package/application gates remain intact.
verify_trx() {
  python3 - "$1" <<'PY'
import collections
import sys
import xml.etree.ElementTree as ET

minimum = {
    "ProGPU.Wpf.Tests.Composition.Mil.WpfLayoutClipKeyTests": 97,
    "ProGPU.Wpf.Tests.Composition.Mil.WpfLayoutClipKeyEqualityTests": 6,
    "ProGPU.Wpf.Tests.Composition.Mil.WpfVisualInvalidationTrackerTests": 38,
    "ProGPU.Wpf.Tests.Composition.Mil.WpfVisualTreeRendererTests": 242,
    "ProGPU.Wpf.Tests.ProGpuWpfWindowHostTests": 236,
    "ProGPU.Wpf.Tests.PassiveIdleIntervalTests": 8,
    "ProGPU.Wpf.Tests.ShowcasePassiveIdleSourceContractTests": 2,
}
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

def require(condition, message):
    if not condition:
        raise ValueError(message)

try:
    root = ET.parse(sys.argv[1]).getroot()
    summary = root.find("t:ResultSummary", ns)
    require(summary is not None and summary.get("outcome") == "Completed",
            "TRX must report a completed, successful run")
    counters = summary.find("t:Counters", ns)
    require(counters is not None, "TRX counters are missing")
    total = int(counters.attrib["total"])
    require(total >= sum(minimum.values()), "Fewer than 629 tests executed")
    for name in ("executed", "passed"):
        require(int(counters.attrib[name]) == total, f"TRX {name} differs from total")
    for name, value in counters.attrib.items():
        if name not in ("total", "executed", "passed"):
            require(int(value) == 0, f"Nonzero TRX counter: {name}={value}")

    definitions = {}
    for definition in root.findall("t:TestDefinitions/t:UnitTest", ns):
        test_id = definition.get("id")
        method = definition.find("t:TestMethod", ns)
        require(test_id and test_id not in definitions and method is not None,
                "Missing or duplicate TRX test definition")
        definitions[test_id] = method.get("className")

    counts = collections.Counter()
    seen = set()
    executions = set()
    results = root.findall("t:Results/t:UnitTestResult", ns)
    require(len(results) == total, "TRX result count differs from counters")
    for result in results:
        test_id = result.get("testId")
        execution_id = result.get("executionId")
        require(test_id and execution_id and execution_id not in executions,
                "Missing or repeated test execution")
        seen.add(test_id)
        executions.add(execution_id)
        require(result.get("outcome") == "Passed",
                f"Non-passing result: {result.get('testName')} ({result.get('outcome')})")
        class_name = definitions.get(test_id)
        require(class_name in minimum, f"Unexpected or missing test class: {class_name}")
        counts[class_name] += 1
    require(seen == set(definitions), "TRX contains unexecuted test definitions")
    for class_name, expected in minimum.items():
        require(counts[class_name] >= expected,
                f"{class_name}: expected at least {expected}, found {counts[class_name]}")
        print(f"{class_name}: {counts[class_name]} passed (minimum {expected})")
    print(f"Retained invalidation contracts: {total} passed, zero failures/skips")
except (OSError, ET.ParseError, ValueError, KeyError) as error:
    print(f"Retained invalidation TRX validation failed: {error}", file=sys.stderr)
    sys.exit(1)
PY
}

# Receipt verification is independently testable without compiling or executing
# product code. CI always uses the no-argument build-and-test path below.
if [[ $# -eq 2 && "$1" == "--verify-trx" ]]; then
  verify_trx "$2"
  exit 0
elif [[ $# -ne 0 ]]; then
  echo "Usage: $0 [--verify-trx path]" >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"
if [[ -n "${PROGPU_WPF_LAYOUT_CLIP_DOTNET+set}" ]]; then
  dotnet_command="${PROGPU_WPF_LAYOUT_CLIP_DOTNET}"
  if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
    echo "PROGPU_WPF_LAYOUT_CLIP_DOTNET must name an executable file: ${dotnet_command}" >&2
    exit 2
  fi
elif [[ -x "${repo_root}/.dotnet/dotnet" ]]; then
  dotnet_command="${repo_root}/.dotnet/dotnet"
else
  dotnet_command="$(command -v dotnet)"
fi

mkdir -p "${repo_root}/artifacts/layout-clip-ci"
evidence="$(mktemp -d "${repo_root}/artifacts/layout-clip-ci/run.XXXXXX")"
echo "Retained invalidation evidence: ${evidence}"
git rev-parse HEAD > "${evidence}/source-commit.txt"
git submodule status external/ProGPU > "${evidence}/source-dependency.txt"
"${dotnet_command}" --info 2>&1 | tee "${evidence}/dotnet-info.log"

project="${repo_root}/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj"
assembly="${repo_root}/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll"
"${dotnet_command}" build "${project}" --configuration Release \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false --verbosity minimal \
  2>&1 | tee "${evidence}/build.log"

filter='FullyQualifiedName~ProGPU.Wpf.Tests.Composition.Mil.WpfLayoutClipKeyTests.'
filter+='|FullyQualifiedName~ProGPU.Wpf.Tests.Composition.Mil.WpfLayoutClipKeyEqualityTests.'
filter+='|FullyQualifiedName~ProGPU.Wpf.Tests.Composition.Mil.WpfVisualInvalidationTrackerTests.'
filter+='|FullyQualifiedName~ProGPU.Wpf.Tests.Composition.Mil.WpfVisualTreeRendererTests.'
filter+='|FullyQualifiedName~ProGPU.Wpf.Tests.ProGpuWpfWindowHostTests.'
filter+='|FullyQualifiedName~ProGPU.Wpf.Tests.PassiveIdleIntervalTests.'
filter+='|FullyQualifiedName~ProGPU.Wpf.Tests.ShowcasePassiveIdleSourceContractTests.'

# Use VSTest explicitly: the repository's MTP default is not this xUnit adapter.
# Preserve its actual nonzero status even when receipt validation also fails.
set +e
"${dotnet_command}" vstest "${assembly}" \
  "--TestCaseFilter:${filter}" \
  "--Logger:trx;LogFileName=retained-invalidation.trx" \
  "--ResultsDirectory:${evidence}" \
  2>&1 | tee "${evidence}/tests.log"
test_pipeline=("${PIPESTATUS[@]}")
test_status=${test_pipeline[0]}
log_status=${test_pipeline[1]}
verify_trx "${evidence}/retained-invalidation.trx" 2>&1 | tee "${evidence}/verification.log"
receipt_status=$?
set -e
if [[ ${test_status} -ne 0 ]]; then
  exit "${test_status}"
fi
if [[ ${log_status} -ne 0 ]]; then
  exit "${log_status}"
fi
exit "${receipt_status}"
