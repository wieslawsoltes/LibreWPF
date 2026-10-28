#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 6 ]]; then
  echo "usage: $0 <ProGPU commit> <runtime staging directory> [DX12 staging directory] [Xlib staging directory] [Windows staging directory] [ProGPU source root]" >&2
  exit 2
fi

progpu_commit="$1"
runtime_stage="$2"
dx12_stage="${3:-}"
xlib_stage="${4:-}"
windows_stage="${5:-}"
producer_root="${6:-}"
if [[ -n "${windows_stage}" && -z "${producer_root}" || -z "${windows_stage}" && -n "${producer_root}" ]]; then
  echo "Windows staging requires both its destination and exact producer source root." >&2
  exit 2
fi
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
progpu_repository="${PROGPU_CI_REPOSITORY:-wieslawsoltes/ProGPU}"
poll_seconds="${PROGPU_CI_POLL_SECONDS:-20}"
timeout_seconds="${PROGPU_CI_TIMEOUT_SECONDS:-5400}"

for command in find gh jq unzip python3; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "${command} is required to stage exact ProGPU CI runtimes." >&2
    exit 1
  }
done

if [[ ! "${progpu_commit}" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "ProGPU commit must be a full 40-character SHA." >&2
  exit 2
fi
if [[ ! "${poll_seconds}" =~ ^[1-9][0-9]*$ ]] ||
   [[ ! "${timeout_seconds}" =~ ^[1-9][0-9]*$ ]]; then
  echo "ProGPU CI polling values must be positive integers." >&2
  exit 2
fi

started_at="$(date +%s)"
run_id=""
run_url=""
query_failures=0
while true; do
  if ! runs="$(
    gh api \
      "repos/${progpu_repository}/actions/workflows/build.yml/runs?head_sha=${progpu_commit}&per_page=100"
  )"; then
    query_failures=$((query_failures + 1))
    now="$(date +%s)"
    if (( query_failures >= 3 || now - started_at >= timeout_seconds )); then
      echo "Unable to query exact ProGPU Build for ${progpu_commit} after ${query_failures} consecutive request failures." >&2
      exit 1
    fi
    echo "Retrying exact ProGPU Build query after request failure ${query_failures}/3..." >&2
    sleep "${poll_seconds}"
    continue
  fi
  query_failures=0
  # A push to the default branch can start a duplicate Build for an already
  # qualified immutable commit. Reuse a whole completed successful Build while
  # that duplicate is pending; never choose partial jobs or another commit.
  # A newer completed failure remains authoritative and must fail below.
  selected_run="$(
    jq -c \
      --arg sha "${progpu_commit}" \
      '[.workflow_runs[] | select(.head_sha == $sha)]
       | sort_by(.created_at) as $runs
       | $runs[-1] as $latest
       | ([$runs[] | select(.status == "completed")] | last) as $completed
       | if $latest.status == "completed" then $latest
         elif $completed.conclusion == "success" then $completed
         else $latest
         end
       | . // {}' \
      <<<"${runs}"
  )"
  run_id="$(jq -r '.id // empty' <<<"${selected_run}")"
  status="$(jq -r '.status // empty' <<<"${selected_run}")"
  conclusion="$(jq -r '.conclusion // empty' <<<"${selected_run}")"
  run_url="$(jq -r '.html_url // empty' <<<"${selected_run}")"

  if [[ "${status}" == "completed" ]]; then
    if [[ "${conclusion}" != "success" ]]; then
      echo "Exact ProGPU Build ${run_url:-${run_id}} completed with ${conclusion:-an unknown result}." >&2
      exit 1
    fi
    break
  fi

  now="$(date +%s)"
  if (( now - started_at >= timeout_seconds )); then
    echo "Timed out waiting for a successful ProGPU Build for ${progpu_commit} (${run_url:-no run found})." >&2
    exit 1
  fi
  echo "Waiting for exact ProGPU Build ${run_url:-for ${progpu_commit}} to finish..."
  sleep "${poll_seconds}"
done

artifact_id="$(
  gh api \
    "repos/${progpu_repository}/actions/runs/${run_id}/artifacts?per_page=100" \
    --jq '.artifacts[] | select(.name == "progpu-native-package" and .expired == false) | .id' \
    | tail -n 1
)"
if [[ -z "${artifact_id}" ]]; then
  echo "Successful ProGPU Build ${run_url:-${run_id}} has no live progpu-native-package artifact." >&2
  exit 1
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/progpu-ci-native.XXXXXX")"
cleanup() {
  rm -rf "${temporary_root}"
}
trap cleanup EXIT

artifact_archive="${temporary_root}/progpu-native-package.zip"
artifact_contents="${temporary_root}/artifact"
mkdir -p "${artifact_contents}"
gh api \
  -H "Accept: application/vnd.github+json" \
  "repos/${progpu_repository}/actions/artifacts/${artifact_id}/zip" \
  > "${artifact_archive}"
unzip -q "${artifact_archive}" -d "${artifact_contents}"

native_packages=()
while IFS= read -r -d '' package; do
  native_packages+=("${package}")
done < <(find "${artifact_contents}" -type f -name 'ProGPU.Backend.Native.*.nupkg' -print0)
if [[ "${#native_packages[@]}" != "1" ]]; then
  echo "The exact ProGPU artifact must contain one ProGPU.Backend.Native package, found ${#native_packages[@]}." >&2
  exit 1
fi
native_package="${native_packages[0]}"
package_version="${native_package##*/ProGPU.Backend.Native.}"
package_version="${package_version%.nupkg}"
sdk_package="${native_package%/*}/ProGPU.Native.Sdk.${package_version}.nupkg"
stage_arguments=("${native_package}" "${sdk_package}" "${runtime_stage}"
  --version "${package_version}" --commit "${progpu_commit}")
if [[ -n "${dx12_stage:-}" ]]; then
  stage_arguments+=(--dx12-package "${native_package%/*}/ProGPU.Backend.Dx12.${package_version}.nupkg"
    --dx12-destination "${dx12_stage}")
fi
if [[ -n "${xlib_stage:-}" ]]; then
  stage_arguments+=(--xlib-package "${native_package%/*}/ProGPU.Backend.${package_version}.nupkg"
    --xlib-destination "${xlib_stage}")
fi
if [[ -n "${windows_stage:-}" ]]; then
  stage_arguments+=(--windows-package "${native_package%/*}/ProGPU.Backend.${package_version}.nupkg"
    --windows-destination "${windows_stage}" --producer-root "${producer_root}")
fi
python3 "${script_dir}/progpu-stage-release-native-packages.py" "${stage_arguments[@]}"

required_runtimes=(
  runtimes/linux-x64/native/libprogpu_native.so
  runtimes/linux-x64/native/libprogpu_native_dawn.so
  runtimes/linux-arm64/native/libprogpu_native.so
  runtimes/linux-arm64/native/libprogpu_native_dawn.so
  runtimes/osx-x64/native/libprogpu_native.dylib
  runtimes/osx-x64/native/libprogpu_native_dawn.dylib
  runtimes/osx-arm64/native/libprogpu_native.dylib
  runtimes/osx-arm64/native/libprogpu_native_dawn.dylib
  runtimes/win-x64/native/progpu_native.dll
  runtimes/win-x64/native/progpu_native_dawn.dll
  runtimes/win-x64/native/progpu_native_direct2d.dll
  runtimes/win-arm64/native/progpu_native.dll
  runtimes/win-arm64/native/progpu_native_dawn.dll
  runtimes/win-arm64/native/progpu_native_direct2d.dll
)
for runtime in "${required_runtimes[@]}"; do
  if [[ ! -f "${runtime_stage}/${runtime}" ]]; then
    echo "The exact ProGPU artifact is missing ${runtime}." >&2
    exit 1
  fi
done

echo "Staged exact ProGPU ${progpu_commit} native runtimes from ${run_url}."
