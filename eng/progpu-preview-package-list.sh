#!/usr/bin/env bash

progpu_preview_runtime_package_ids=(
  ProGPU.Backend
  ProGPU.Backend.Dawn
  ProGPU.Text.Shaping
  ProGPU.DirectX
  ProGPU.Transpiler
  ProGPU.Compute
  ProGPU.Vector
  ProGPU.Text
  ProGPU.Scene
  ProGPU.Layout
  ProGPU.Virtualization
  ProGPU.WinRT
  ProGPU.Media
  ProGPU.Media.Scene
  ProGPU.WinUI
  ProGPU.Avalonia
  ProGPU.SkiaSharp
  ProGPU.System.Drawing.Common
  LibreWPF.Interop
)

progpu_preview_package_ids=(
  LibreWPF.Transport
  "${progpu_preview_runtime_package_ids[@]}"
  LibreWPF.ProGPU
  LibreWPF.Sdk
)

progpu_preview_package_version() {
  local package_id="$1"
  case "${package_id}" in
    LibreWPF.Transport|LibreWPF.ProGPU|LibreWPF.Sdk)
      printf '%s\n' "${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.43}"
      ;;
    *)
      printf '%s\n' "${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.53}"
      ;;
  esac
}

progpu_preview_package_file_name() {
  local package_id="$1"
  printf '%s.%s.nupkg\n' "${package_id}" "$(progpu_preview_package_version "${package_id}")"
}
