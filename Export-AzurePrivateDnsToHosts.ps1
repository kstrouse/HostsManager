[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$OutputPath = ".\switchhosts-private-dns.hosts",

    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $false)]
    [string]$SubscriptionName = 'sub-network',

    [Parameter(Mandatory = $false)]
    [string[]]$ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string[]]$ZoneName,

    [Parameter(Mandatory = $false)]
    [switch]$Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-AzCli {
    $az = Get-Command az -ErrorAction SilentlyContinue
    if (-not $az) {
        throw "Azure CLI ('az') was not found in PATH. Install it first: https://learn.microsoft.com/cli/azure/install-azure-cli"
    }
}

function Invoke-AzJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    $stderrFile = [System.IO.Path]::GetTempFileName()

    try {
        $output = & az @Args --output json --only-show-errors 2> $stderrFile
        $stderr = Get-Content -LiteralPath $stderrFile -Raw -ErrorAction SilentlyContinue
    }
    finally {
        Remove-Item -LiteralPath $stderrFile -Force -ErrorAction SilentlyContinue
    }

    if ($output -is [System.Array]) {
        $output = ($output -join [Environment]::NewLine)
    }

    if ($LASTEXITCODE -ne 0) {
        throw "az command failed: az $($Args -join ' ')`n$stderr$output"
    }

    if ([string]::IsNullOrWhiteSpace($output)) {
        return @()
    }

    try {
        return $output | ConvertFrom-Json
    }
    catch {
        throw "Failed to parse JSON from az command: az $($Args -join ' ')`nRaw output:`n$output"
    }
}

function Resolve-RecordFqdn {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativeName,
        [Parameter(Mandatory = $true)]
        [string]$ZoneNameValue
    )

    if ($RelativeName -eq '@') {
        return $ZoneNameValue.ToLowerInvariant()
    }

    if ($RelativeName.ToLowerInvariant().EndsWith(".$($ZoneNameValue.ToLowerInvariant())")) {
        return $RelativeName.ToLowerInvariant()
    }

    return "$RelativeName.$ZoneNameValue".ToLowerInvariant()
}

Test-AzCli

Write-Host "Checking Azure login..."
$null = Invoke-AzJson -Args @('account', 'show')

$targetSubscription = if (-not [string]::IsNullOrWhiteSpace($SubscriptionId)) { $SubscriptionId } else { $SubscriptionName }

if (-not [string]::IsNullOrWhiteSpace($targetSubscription)) {
    Write-Host "Selecting subscription: $targetSubscription"
    $subscriptions = @((Invoke-AzJson -Args @('account', 'list')))
    $match = $subscriptions | Where-Object {
        ([string]$_.id).Equals($targetSubscription, [System.StringComparison]::OrdinalIgnoreCase) -or
        ([string]$_.name).Equals($targetSubscription, [System.StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1

    if (-not $match) {
        throw "Subscription '$targetSubscription' was not found in the current Azure account. Run 'az account list -o table' and confirm the value."
    }

    $null = Invoke-AzJson -Args @('account', 'set', '--subscription', ([string]$match.id))
    Write-Host "Using subscription: $([string]$match.name) ($([string]$match.id))"
}

if ((Test-Path -LiteralPath $OutputPath) -and -not $Overwrite) {
    throw "Output file already exists: $OutputPath. Use -Overwrite to replace it."
}

Write-Host "Loading private DNS zones..."
$allZones = Invoke-AzJson -Args @('network', 'private-dns', 'zone', 'list')

if (-not $allZones) {
    throw "No private DNS zones were found in the current subscription/context."
}

$zones = @($allZones)

$zones = @($zones | Where-Object {
    ([string]$_.name).StartsWith('privatelink', [System.StringComparison]::OrdinalIgnoreCase)
})

if ($ResourceGroup -and $ResourceGroup.Count -gt 0) {
    $resourceGroupSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($rg in $ResourceGroup) {
        [void]$resourceGroupSet.Add($rg)
    }
    $zones = @($zones | Where-Object { $resourceGroupSet.Contains($_.resourceGroup) })
}

if ($ZoneName -and $ZoneName.Count -gt 0) {
    $zoneNameSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($zn in $ZoneName) {
        [void]$zoneNameSet.Add($zn)
    }
    $zones = @($zones | Where-Object { $zoneNameSet.Contains($_.name) })
}

if (-not $zones -or $zones.Count -eq 0) {
    throw "No private DNS zones starting with 'privatelink' matched the provided filters."
}

$timestampUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
$hostLines = New-Object System.Collections.Generic.List[string]
$hostLines.Add("# Generated from Azure Private DNS on $timestampUtc UTC")
$hostLines.Add("# Format is compatible with SwitchHosts (standard hosts entries)")

$totalRecords = 0
$totalHostEntries = 0

foreach ($zone in $zones) {
    $zoneNameValue = [string]$zone.name
    $zoneResourceGroup = [string]$zone.resourceGroup

    Write-Host "Reading A records from zone '$zoneNameValue' (RG: $zoneResourceGroup)..."
    $recordSets = Invoke-AzJson -Args @(
        'network', 'private-dns', 'record-set', 'a', 'list',
        '--resource-group', $zoneResourceGroup,
        '--zone-name', $zoneNameValue
    )

    if (-not $recordSets) {
        continue
    }

    $hostLines.Add("")
    $hostLines.Add("# Zone: $zoneNameValue (Resource Group: $zoneResourceGroup)")

    foreach ($recordSet in $recordSets) {
        $totalRecords++

        if (-not $recordSet.arecords -or $recordSet.arecords.Count -eq 0) {
            continue
        }

        $fqdn = Resolve-RecordFqdn -RelativeName ([string]$recordSet.name) -ZoneNameValue $zoneNameValue

        foreach ($aRecord in $recordSet.arecords) {
            $ip = [string]$aRecord.ipv4Address
            if ([string]::IsNullOrWhiteSpace($ip)) {
                continue
            }

            $line = "{0}`t{1}" -f $ip, $fqdn
            $hostLines.Add($line)
            $totalHostEntries++
        }
    }
}

if ($totalHostEntries -eq 0) {
    throw "No A records with IPv4 addresses were found in the selected private DNS zones."
}

$outDir = Split-Path -Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -Path $outDir -ItemType Directory -Force | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $hostLines -Encoding UTF8

Write-Host ""
Write-Host "Done. Wrote $totalHostEntries hosts entries from $totalRecords A record sets."
Write-Host "Output file: $((Resolve-Path -LiteralPath $OutputPath).Path)"
