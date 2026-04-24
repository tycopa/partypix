<#
.SYNOPSIS
    Installs PartyPix as a Windows Service pointing at a published build.

.DESCRIPTION
    Expects the app to have been published (dotnet publish -c Release -o <path>).
    Registers a Windows Service that runs PartyPix.Web.exe, starts it, and grants
    Network Service write access to App_Data for the SQLite DB and uploads.

.EXAMPLE
    .\install-service.ps1 -PublishPath C:\apps\partypix -ServiceUser "NT AUTHORITY\NetworkService"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishPath,
    [string]$ServiceName = "PartyPix",
    [string]$DisplayName = "PartyPix",
    [string]$ServiceUser = "NT AUTHORITY\NetworkService",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $PublishPath "PartyPix.Web.exe"
if (-not (Test-Path $exe)) {
    throw "PartyPix.Web.exe not found at $exe. Run 'dotnet publish -c Release -o $PublishPath' first."
}

# Stop and remove existing service if present
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Pin Kestrel to localhost:$Port so only cloudflared can reach it
$envFile = Join-Path $PublishPath "appsettings.Production.json"
if (-not (Test-Path $envFile)) {
    @{
        Kestrel = @{
            Endpoints = @{
                Http = @{ Url = "http://127.0.0.1:$Port" }
            }
        }
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $envFile -Encoding UTF8
}

New-Service -Name $ServiceName `
            -BinaryPathName "`"$exe`"" `
            -DisplayName $DisplayName `
            -StartupType Automatic `
            -Description "PartyPix — QR event photo sharing" `
            -Credential $null | Out-Null

# Grant the service identity access to the content root
$acl = Get-Acl $PublishPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $ServiceUser, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($rule)
Set-Acl -Path $PublishPath -AclObject $acl

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' installed and started on http://127.0.0.1:$Port"
