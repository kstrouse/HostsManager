# Azure Private DNS -> SwitchHosts file

This workspace contains a PowerShell script that exports `A` records from Azure Private DNS zones into a standard hosts-format file that is compatible with SwitchHosts.

Only zones whose names start with `privatelink` are exported.

## File

- `Export-AzurePrivateDnsToHosts.ps1`

## Prerequisites

- PowerShell 5.1+ or PowerShell 7+
- Azure CLI (`az`) installed
- Logged in to Azure: `az login`

## Usage

From this folder:

```powershell
./Export-AzurePrivateDnsToHosts.ps1 -OutputPath .\switchhosts-private-dns.hosts -Overwrite
```

By default, the script targets the subscription named `sub-network`.

### Optional filters

Export only selected resource groups:

```powershell
./Export-AzurePrivateDnsToHosts.ps1 -ResourceGroup rg-network,rg-shared -OutputPath .\switchhosts-private-dns.hosts -Overwrite
```

Export only selected zones:

```powershell
./Export-AzurePrivateDnsToHosts.ps1 -ZoneName privatelink.database.windows.net,privatelink.blob.core.windows.net -OutputPath .\switchhosts-private-dns.hosts -Overwrite
```

Use a specific subscription:

```powershell
./Export-AzurePrivateDnsToHosts.ps1 -SubscriptionId <subscription-guid> -OutputPath .\switchhosts-private-dns.hosts -Overwrite
```

Or override by subscription name:

```powershell
./Export-AzurePrivateDnsToHosts.ps1 -SubscriptionName <subscription-name> -OutputPath .\switchhosts-private-dns.hosts -Overwrite
```

## Result format

The output file is plain hosts syntax:

```text
10.20.30.40    myserver.privatelink.database.windows.net
10.20.30.41    mystorage.privatelink.blob.core.windows.net
```

You can import/use this file directly in SwitchHosts.

---

## Hosts Manager (.NET 10)

This workspace now also includes a cross-platform desktop hosts manager app written in C# with Avalonia:

- Project: `HostsManager/HostsManager.csproj`
- Framework: `net10.0`
- Platforms: Windows, macOS, Linux

### Run

```powershell
dotnet run --project .\HostsManager\HostsManager.csproj
```

### Build

```powershell
dotnet build .\HostsManager\HostsManager.csproj
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
