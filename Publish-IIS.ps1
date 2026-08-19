#
# Builds a ready-to-copy IIS deployment folder in .\publish\  (command-line equivalent of clicking
# "Publish" on the Api project in Visual Studio using the FolderProfile — same output either way,
# since Api.csproj's CopyFrontendAfterPublish target does the Frontend copy automatically now.)
#
#   publish\            -> IIS site root (static Frontend files)
#   publish\api\         -> IIS sub-application "/api" (published Backend/Api, incl. web.config)
#
# Usage:
#   .\Publish-IIS.ps1                # framework-dependent (needs ASP.NET Core 8 Hosting Bundle on the server)
#   .\Publish-IIS.ps1 -SelfContained # bundles the .NET runtime (no Hosting Bundle needed, bigger output)
#
# Then on the IIS server: copy the whole `publish` folder to the site's physical path, and in
# IIS Manager convert the `api` subfolder into an Application (right-click -> Convert to Application)
# so it gets its own app pool. See Setup-IIS.ps1 for the MySQL connection string setup
# (DatabaseProvider / ConnectionStrings:DefaultConnection via environment variables on
# the App Pool — don't commit real credentials).
#

param(
    [switch]$SelfContained,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$outDir = Join-Path $root "publish"
$apiOut = Join-Path $outDir "api"

Write-Host "==> Cleaning $outDir"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

Write-Host "==> Publishing Backend/Api ($(if ($SelfContained) { "self-contained $Runtime" } else { "framework-dependent" }))"
$apiProject = Join-Path $root "Backend\Api\Api.csproj"
if ($SelfContained) {
    dotnet publish $apiProject -c Release -o $apiOut -r $Runtime --self-contained true -p:PublishSingleFile=false
} else {
    dotnet publish $apiProject -c Release -o $apiOut --self-contained false
}
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
# Frontend static files are copied automatically by Api.csproj's CopyFrontendAfterPublish target.

Write-Host "==> Done. Deployment package ready at: $outDir"
Write-Host "    Site root      : $outDir"
Write-Host "    /api sub-app   : $apiOut"
