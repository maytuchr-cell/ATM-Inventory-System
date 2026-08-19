#
# Run this ON THE SERVER (elevated PowerShell), after copying the `publish` folder to
# C:\playground\ATM-Inventory-System\publish
#
# Sets up:
#   - IIS Site "ATM-Inventory" -> C:\playground\ATM-Inventory-System\publish  (Frontend, static)
#   - IIS Application "/api"   -> ...\publish\api  (Backend API, its own App Pool, No Managed Code)
#   - MySQL connection string + DatabaseProvider as durable App Pool environment variables
#     (survive re-copying `publish` on future deploys, since they live in IIS config, not in the files)
#
# Prereqs on this server:
#   - IIS with ASP.NET Core Module v2 installed (comes with the ASP.NET Core 8 Hosting Bundle,
#     https://dotnet.microsoft.com/download/dotnet/8.0 -> "Hosting Bundle")
#   - Windows Server 2019+ (for AppPool-level EnvironmentVariables support)
#
# Usage:
#   .\Setup-IIS.ps1 -MySqlConnection "server=172.22.100.22;port=3306;database=Sparepart_DB;user=workbench_user;password=...;connectiontimeout=60;"
#   .\Setup-IIS.ps1 -AssetPath "D:\ATMAssets" -MySqlConnection "..."   # only if this server has a D: drive and you want part images stored outside the publish folder
#

param(
    [string]$SiteName       = "ATM-Inventory",
    [string]$PhysicalPath   = "C:\playground\ATM-Inventory-System\publish",
    [string]$Port           = 80,
    [string]$ApiAppPoolName = "ATM-Inventory-API",
    # Leave blank (default) to let the app store part images/attachments under its own
    # <api>\wwwroot folder — always exists, no drive-letter assumptions. Only pass -AssetPath
    # if you specifically want them on external storage (e.g. a dedicated data drive).
    [string]$AssetPath      = "",
    [Parameter(Mandatory = $true)]
    [string]$MySqlConnection
)

Import-Module WebAdministration -ErrorAction Stop

$apiPhysicalPath = Join-Path $PhysicalPath "api"
if (-not (Test-Path $apiPhysicalPath)) {
    throw "Not found: $apiPhysicalPath — copy the published `publish` folder here first."
}

# ── Site (Frontend, static files) ──
if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
    Write-Host "==> Creating site '$SiteName' -> $PhysicalPath (port $Port)"
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -Port $Port -Force | Out-Null
} else {
    Write-Host "==> Site '$SiteName' already exists, updating physical path"
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
}

# ── App Pool for the API (No Managed Code — ANCM handles the .NET runtime) ──
if (-not (Test-Path "IIS:\AppPools\$ApiAppPoolName")) {
    Write-Host "==> Creating app pool '$ApiAppPoolName'"
    New-WebAppPool -Name $ApiAppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$ApiAppPoolName" -Name managedRuntimeVersion -Value ""

# ── Durable environment variables on the App Pool (survive future re-deploys of `publish`) ──
# Names match appsettings.json's flat "DatabaseProvider" key and "ConnectionStrings:DefaultConnection"
# (ASP.NET Core maps env var "__" to config ":" — a flat key has no "__" prefix at all).
Write-Host "==> Setting App Pool environment variables (DatabaseProvider, MySQL connection string$(if ($AssetPath) { ', AssetPath' }))"
Clear-ItemProperty "IIS:\AppPools\$ApiAppPoolName" -Name environmentVariables -ErrorAction SilentlyContinue
Add-WebConfigurationProperty -PSPath "IIS:\" `
    -Filter "system.applicationHost/applicationPools/add[@name='$ApiAppPoolName']/environmentVariables" `
    -Name "." -Value @{ name = "DatabaseProvider"; value = "MySql" }
Add-WebConfigurationProperty -PSPath "IIS:\" `
    -Filter "system.applicationHost/applicationPools/add[@name='$ApiAppPoolName']/environmentVariables" `
    -Name "." -Value @{ name = "ConnectionStrings__DefaultConnection"; value = $MySqlConnection }
if ($AssetPath) {
    Add-WebConfigurationProperty -PSPath "IIS:\" `
        -Filter "system.applicationHost/applicationPools/add[@name='$ApiAppPoolName']/environmentVariables" `
        -Name "." -Value @{ name = "AssetPath"; value = $AssetPath }
}

# ── /api Application ──
if (-not (Get-WebApplication -Site $SiteName -Name "api" -ErrorAction SilentlyContinue)) {
    Write-Host "==> Creating IIS Application '/api' -> $apiPhysicalPath"
    New-WebApplication -Site $SiteName -Name "api" -PhysicalPath $apiPhysicalPath -ApplicationPool $ApiAppPoolName | Out-Null
} else {
    Write-Host "==> Application '/api' already exists, updating physical path + app pool"
    Set-ItemProperty "IIS:\Sites\$SiteName\api" -Name physicalPath -Value $apiPhysicalPath
    Set-ItemProperty "IIS:\Sites\$SiteName\api" -Name applicationPool -Value $ApiAppPoolName
}

# ── Permissions: app pool identity needs write access for uploads/attachments (either the
#    default <api>\wwwroot, or -AssetPath if given) and ANCM stdout logs ──
$identity = "IIS AppPool\$ApiAppPoolName"
foreach ($dir in @("wwwroot", "wwwroot\uploads", "logs")) {
    $path = Join-Path $apiPhysicalPath $dir
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    Write-Host "==> Granting Modify to '$identity' on $path"
    icacls $path /grant "${identity}:(OI)(CI)M" | Out-Null
}
if ($AssetPath) {
    New-Item -ItemType Directory -Path $AssetPath -Force | Out-Null
    Write-Host "==> Granting Modify to '$identity' on $AssetPath"
    icacls $AssetPath /grant "${identity}:(OI)(CI)M" | Out-Null
}

Write-Host ""
Write-Host "==> Done."
Write-Host "    Site : http://<server>:$Port/"
Write-Host "    API  : http://<server>:$Port/api/Auth/login  (no Swagger UI is registered in Program.cs)"
Write-Host ""
Write-Host "    Reminder: make sure the ASP.NET Core 8 Hosting Bundle is installed, then"
Write-Host "    'iisreset' once so ANCM picks up the new app pool."
