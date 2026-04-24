<#
.SYNOPSIS
    Creates (or recreates) the PartyPix IIS app pool and site, binds it to
    127.0.0.1:<Port>, and grants the app pool identity write access to the
    published folder so App_Data logs and tus temp files work out of the box.

.DESCRIPTION
    Run on the Windows VM after:
      1. Installing IIS (Web-Server) with the WebSockets feature
         (Web-WebSockets) enabled.
      2. Installing the .NET 10 Hosting Bundle (provides AspNetCoreModuleV2).
      3. Publishing the app: dotnet publish -c Release -o <PublishPath>.

    The site binds to 127.0.0.1 so the process is only reachable via
    cloudflared, which you configure separately to point at
    http://localhost:<Port>.

.EXAMPLE
    .\install-iis.ps1 -PublishPath C:\apps\partypix -Port 5000
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishPath,
    [string]$SiteName = "PartyPix",
    [string]$AppPoolName = "PartyPix",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $PublishPath "PartyPix.Web.exe"))) {
    throw "PartyPix.Web.exe not found in $PublishPath. Publish the app there first."
}
if (-not (Test-Path (Join-Path $PublishPath "web.config"))) {
    throw "web.config not found in $PublishPath. Ensure the publish step ran."
}

Import-Module WebAdministration

# -- App pool -----------------------------------------------------------
if (Test-Path "IIS:\AppPools\$AppPoolName") {
    Write-Host "Removing existing app pool '$AppPoolName'"
    Remove-WebAppPool -Name $AppPoolName
}

Write-Host "Creating app pool '$AppPoolName'"
New-WebAppPool -Name $AppPoolName | Out-Null

# "No Managed Code" — required for .NET (Core) apps via AspNetCoreModuleV2
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)

# -- Site ---------------------------------------------------------------
if (Test-Path "IIS:\Sites\$SiteName") {
    Write-Host "Removing existing site '$SiteName'"
    Remove-Website -Name $SiteName
}

Write-Host "Creating site '$SiteName' -> $PublishPath on 127.0.0.1:$Port"
New-Website -Name $SiteName `
            -PhysicalPath $PublishPath `
            -ApplicationPool $AppPoolName `
            -IPAddress "127.0.0.1" `
            -Port $Port `
            -HostHeader "" `
            -Force | Out-Null

# Always-running requires preloadEnabled on the app
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true

# -- Permissions --------------------------------------------------------
$identity = "IIS AppPool\$AppPoolName"
Write-Host "Granting '$identity' modify access to $PublishPath"
$acl = Get-Acl $PublishPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $identity, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($rule)
Set-Acl -Path $PublishPath -AclObject $acl

# If Storage:RootPath in appsettings points elsewhere (e.g. D:\partypix-media),
# grant the same identity Modify on that path manually. Same for Tus:TempPath.

# -- Start --------------------------------------------------------------
Start-WebAppPool -Name $AppPoolName
Start-Website -Name $SiteName

Write-Host ""
Write-Host "Done. Site listening on http://127.0.0.1:$Port"
Write-Host "Point cloudflared at http://localhost:$Port"
