# LibreWPF Hello App

This is the smallest runnable SDK-switched WPF app in the repo. The project file uses only:

- `Project Sdk="LibreWPF.Sdk/0.1.0-preview.45"`
- `TargetFramework=net10.0-windows`
- `UseWPF=true`

The app uses compiled `App.xaml`, `StartupUri`, a compiled `Window`, basic binding, collection binding, dynamic resources, and a button event handler without any app-side ProGPU APIs.

Build and launch the SDK-produced apphost from the repository root:

```bash
./eng/run-progpu-wpf-hello.sh
```

Run a bounded `Application.Run()` validation through the same apphost:

```bash
PROGPU_WPF_HELLO_RUN_VALIDATE=1 ./eng/run-progpu-wpf-hello.sh
```

Run a live ProGPU/Silk.NET geometry validation through the same apphost:

```bash
PROGPU_WPF_HELLO_LIVE_VALIDATE=1 ./eng/run-progpu-wpf-hello.sh
```

If the local `0.1.0-preview.45` LibreWPF packages are stale or missing, rebuild the SDK package feed first:

```bash
PROGPU_WPF_HELLO_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-hello.sh
```
