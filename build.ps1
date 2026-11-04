# Builds and packages the Offline Bodies mod into offlinebodies_<version>.zip
# Usage (in a normal PowerShell terminal, NOT inside Claude Code):
#   pwsh -ExecutionPolicy Bypass -File .\build.ps1
# Optionally set the Vintage Story install folder first:
#   $env:VINTAGE_STORY = "C:\Users\you\AppData\Roaming\Vintagestory"

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

# --- find the Vintage Story install (the folder that has VintagestoryAPI.dll) ---
$vsDir = $env:VINTAGE_STORY
if (-not $vsDir -or -not (Test-Path (Join-Path $vsDir "VintagestoryAPI.dll"))) {
    $candidates = @(
        (Join-Path $env:APPDATA "Vintagestory"),
        "C:\Users\$env:USERNAME\AppData\Roaming\Vintagestory",
        "$env:ProgramFiles\Vintagestory",
        "${env:ProgramFiles(x86)}\Vintagestory"
    )
    $vsDir = $null
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c "VintagestoryAPI.dll"))) { $vsDir = $c; break }
    }
}
if (-not $vsDir) {
    throw "Could not find the Vintage Story install (VintagestoryAPI.dll). Set `$env:VINTAGE_STORY to it and re-run."
}
Write-Host "Vintage Story install: $vsDir"

# --- build ---
dotnet build "$root\OfflineBodies.csproj" -c Release -p:VintageStoryInstallDir="$vsDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed (see errors above)." }

$dll = "$root\bin\Release\offlinebodies.dll"
if (-not (Test-Path $dll)) { throw "Build reported success but $dll is missing." }

# --- package the release zip (dll + modinfo.json + assets\ + license at the root) ---
$version = (Get-Content "$root\modinfo.json" -Raw | ConvertFrom-Json).version
$stage = "$root\_pkg"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Copy-Item $dll $stage
Copy-Item "$root\modinfo.json" $stage
Copy-Item "$root\assets" $stage -Recurse
if (Test-Path "$root\LICENSE-DeathCorpses.txt") { Copy-Item "$root\LICENSE-DeathCorpses.txt" $stage }

$zip = "$root\offlinebodies_$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force

# --- report ---
Write-Host ""
Write-Host "==== DONE ===="
Write-Host ("Version:  {0}" -f $version)
Write-Host ("DLL size: {0} bytes" -f (Get-Item $dll).Length)
Write-Host ("Zip:      {0}" -f $zip)
Write-Host "Zip contents:"
Add-Type -AssemblyName System.IO.Compression.FileSystem
([System.IO.Compression.ZipFile]::OpenRead($zip)).Entries | ForEach-Object { Write-Host ("  " + $_.FullName) }
