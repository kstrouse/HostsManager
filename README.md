# Hosts Manager

Hosts Manager is a desktop app for building and maintaining a managed `hosts` file from multiple sources.

It is written in C# on Avalonia and is intended for people who want a safer way to combine:

- local file-backed hosts sources
- manually edited managed entries
- remote HTTP/HTTPS hosts sources
- Azure Private DNS zones

The app keeps those sources organized, watches for external file changes, and can apply the enabled set into the system hosts file using managed markers.

## Features

- Multiple source types: system, local, and remote
- Local source workflows:
  create a new hosts file, add an existing file, rename it, re-create missing files, open the containing folder
- Remote source workflows:
  manual sync, sync-all, per-source auto-refresh intervals, Azure Private DNS import
- Automatic background reconcile loop for config changes, local file changes, and managed hosts updates
- Direct system-hosts editing mode with an explicit safety toggle
- Backup and restore support for the system hosts file
- Tray integration with show/apply/exit flows
- Syntax highlighting and validation hints in the hosts editor

## Platform Status

- Windows: primary supported platform
- macOS and Linux: code paths exist, but they are not verified to the same degree yet

On Windows, the app runs normally without elevation and only requests administrator approval when a hosts-file update actually needs it.

## Project Layout

- App: [src/HostsManager.Desktop/HostsManager.Desktop.csproj](src/HostsManager.Desktop/HostsManager.Desktop.csproj)
- Tests: [test/HostsManager.Desktop.Tests/HostsManager.Desktop.Tests.csproj](test/HostsManager.Desktop.Tests/HostsManager.Desktop.Tests.csproj)
- Windows installer script: [installer/windows/HostsManager.iss](installer/windows/HostsManager.iss)
- Sample local hosts file: [samplehosts/test-local.hosts](samplehosts/test-local.hosts)

## Requirements

- .NET 10 SDK
- Windows for the installer build flow
- Inno Setup 6 if you want to compile the Windows installer

## Local Development

Run the desktop app:

```powershell
dotnet run --project .\src\HostsManager.Desktop\HostsManager.Desktop.csproj
```

Build the solution:

```powershell
dotnet build HostsManager.slnx
```

Run the test suite:

```powershell
dotnet test HostsManager.slnx
```

## Publishing

Build self-contained publish folders:

```powershell
.\Publish-SelfContained.ps1
```

Default outputs:

- `artifacts/publish/win-x64`
- `artifacts/publish/osx-x64`
- `artifacts/publish/osx-arm64`

Build the Windows installer:

```powershell
.\Build-WindowsInstaller.ps1 -Version 1.0.0
```

Default outputs:

- publish folder: `artifacts/publish/win-x64`
- installer: `artifacts/installer/HostsManager-1.0.0-Setup.exe`

If Inno Setup is not installed at `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`, the script still prepares the publish output and stops before the installer compile step.

## How Hosts Updates Work

Hosts Manager treats the system hosts file as a managed output.

- enabled sources are combined into a managed section
- unmanaged content outside the managed markers is preserved during managed apply flows
- the app keeps track of when background changes need administrator approval
- if a watched local file changes on disk, the app can detect that and refresh the managed state

Direct system-hosts editing is available, but intentionally disabled by default for safety.

## Azure Private DNS

Azure Private DNS support is intended for turning selected private DNS zones into hosts entries.

- connect to Azure and load subscriptions
- choose a subscription for a remote source
- load available zones
- exclude individual zones if needed
- sync the generated entries into that remote source

## Notes Before First Public Release

- The app is currently versioned as `1.0.0` in the project file, but this repository should still be treated as an early public release until you have published binaries and validated the install/update path.
- Windows is the main release target right now.
- macOS and Linux support should be presented as experimental unless you validate those paths yourself.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
