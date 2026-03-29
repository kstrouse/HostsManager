## Hosts Manager

This is a cross-platform desktop hosts manager app written in C# with Avalonia:

- Project: `src/HostsManager.Desktop/HostsManager.Desktop.csproj`
- Framework: `net10.0`
- Platforms: Windows, macOS, Linux

### Run

```powershell
dotnet run --project .\src\HostsManager.Desktop\HostsManager.Desktop.csproj
```

### Build

```powershell
dotnet build .\src\HostsManager.Desktop\HostsManager.Desktop.csproj
```

### Self-contained folder publish

Build self-contained app folders for Windows and macOS:

```powershell
.\Publish-SelfContained.ps1
```

Outputs:

- `artifacts/publish/win-x64`
- `artifacts/publish/osx-x64`
- `artifacts/publish/osx-arm64`

### Windows Installer

Build an installer-ready Windows publish and, if Inno Setup 6 is installed, compile a Windows installer:

```powershell
.\Build-WindowsInstaller.ps1 -Version 1.0.0
```

Outputs:

- Publish folder: `artifacts/publish/win-x64`
- Installer: `artifacts/installer/HostsManager-1.0.0-Setup.exe`

Requirements:

- Inno Setup 6 installed at `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

### What it does

- Manage multiple hosts sources (create/delete/edit/enable)
- Run continuously in the app background loop and manage the system hosts file automatically
- Minimize to system tray when window is closed (app keeps running)
- Provide tray menu actions: Show, Apply Hosts Now, Exit
- Monitor known local source files for external changes and reload them automatically
- Detect in-app source entry changes and automatically re-apply managed hosts entries
- Hosts Entries editor includes syntax highlighting (comments, IPs, hostnames)
- Local source workflow:
	- Create a new local source by picking where to save a file
	- Add an existing local file as a source
	- Reload entries from local file, or save edited entries back to it
- Remote source workflow:
	- Create remote sources with protocol and location settings
	- Sync selected remote source manually or sync all remote sources
	- Auto-refresh remote sources on per-source minute intervals
- Save sources to app config storage
- Apply enabled sources into your system hosts file using managed markers
- Create and restore a hosts backup

### Privileges

Applying or restoring hosts requires elevated privileges:

- Windows:
	- App starts normally and requests elevation only when a hosts-file update needs approval
	- Run-at-startup registration is per-user and does not require startup-time elevation
- macOS/Linux: run with `sudo` as needed
