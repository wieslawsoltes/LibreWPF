#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <ProGPU commit> <runtime staging directory>" >&2
  exit 2
fi

progpu_commit="$1"
runtime_stage="$2"
progpu_repository="${PROGPU_CI_REPOSITORY:-wieslawsoltes/ProGPU}"
poll_seconds="${PROGPU_CI_POLL_SECONDS:-20}"
timeout_seconds="${PROGPU_CI_TIMEOUT_SECONDS:-5400}"

for command in gh jq rg unzip; do
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
while true; do
  runs="$(
    gh api \
      "repos/${progpu_repository}/actions/workflows/build.yml/runs?head_sha=${progpu_commit}&per_page=100"
  )"
  run_id="$(
    jq -r \
      --arg sha "${progpu_commit}" \
      '[.workflow_runs[] | select(.head_sha == $sha)]
       | sort_by(.created_at)
       | last
       | .id // empty' \
      <<<"${runs}"
  )"
  status="$(
    jq -r \
      --arg sha "${progpu_commit}" \
      '[.workflow_runs[] | select(.head_sha == $sha)]
       | sort_by(.created_at)
       | last
       | .status // empty' \
      <<<"${runs}"
  )"
  conclusion="$(
    jq -r \
      --arg sha "${progpu_commit}" \
      '[.workflow_runs[] | select(.head_sha == $sha)]
       | sort_by(.created_at)
       | last
       | .conclusion // empty' \
      <<<"${runs}"
  )"
  run_url="$(
    jq -r \
      --arg sha "${progpu_commit}" \
      '[.workflow_runs[] | select(.head_sha == $sha)]
       | sort_by(.created_at)
       | last
       | .html_url // empty' \
      <<<"${runs}"
  )"

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

native_package="$(
  rg --files "${artifact_contents}" \
    | rg '(^|/)ProGPU\.Backend\.Native\.[^/]+\.nupkg$' \
    | head -n 1
)"
if [[ -z "${native_package}" ]]; then
  echo "The exact ProGPU native-package artifact contains no ProGPU.Backend.Native package." >&2
  exit 1
fi

mkdir -p "${runtime_stage}"
unzip -q -o "${native_package}" 'runtimes/*' -d "${runtime_stage}"

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
