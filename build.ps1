# Builds the mod and packs the release zip in the layout StellarModManager expects:
#   StellarDrive-WorldBackups.zip
#     mod.json
#     Mods/WorldBackups.dll
param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\StellarDrive Demo",
    [string]$MelonLoaderPath = ""
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$args = @("build", "$root\src\WorldBackups.csproj", "-c", "Release", "-p:GamePath=$GamePath")
if ($MelonLoaderPath) { $args += "-p:MelonLoaderPath=$MelonLoaderPath" }
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$stage = Join-Path $root "dist\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$stage\Mods" | Out-Null
Copy-Item "$root\src\bin\Release\WorldBackups.dll" "$stage\Mods\"
Copy-Item "$root\Mods\mod.json" "$stage\"

$zip = Join-Path $root "dist\StellarDrive-WorldBackups.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Write entries by hand: Windows PowerShell's CreateFromDirectory stores "\" separators, which is not valid zip.
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry) | Out-Null
    }
} finally {
    $archive.Dispose()
}
Remove-Item $stage -Recurse -Force
Write-Host "Package: $zip"
