param(
    [string]$SPTPath = 'B:/SPT MOD TESTING'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '../../work/dotnet-home'
$env:APPDATA = Join-Path $PSScriptRoot '../../work/appdata'
$dotnet = Join-Path $PSScriptRoot '../../work/dotnet-sdk/dotnet.exe'

& $dotnet build (Join-Path $PSScriptRoot 'src/MultipurposeDrone.csproj') -c Release "-p:SPTPath=$SPTPath" --nologo
if ($LASTEXITCODE -ne 0)
{
    throw 'Build failed.'
}

& $dotnet run --project (Join-Path $PSScriptRoot 'tests/Checks.csproj') -c Release "-p:SPTPath=$SPTPath" -- $SPTPath (Join-Path $PSScriptRoot 'src/bin/Release/netstandard2.1/Killdg.MultipurposeDrone.dll')
if ($LASTEXITCODE -ne 0)
{
    throw 'Checks failed.'
}

& $dotnet build (Join-Path $PSScriptRoot 'server/MultipurposeDrone.Server.csproj') -c Release "-p:SPTBinaries=$SPTPath/SPT_Runtime" --nologo
if ($LASTEXITCODE -ne 0)
{
    throw 'Server build failed.'
}
& $dotnet run --project (Join-Path $PSScriptRoot 'server-checks/Checks.csproj') -c Release "-p:SPTBinaries=$SPTPath/SPT_Runtime" -- (Join-Path $PSScriptRoot 'server')
if ($LASTEXITCODE -ne 0)
{
    throw 'Server checks failed.'
}

$artifacts = Join-Path $PSScriptRoot 'artifacts'
# Start each build with an empty staging area so obsolete plugins cannot
# carry over into an update archive.
$staging = Join-Path $artifacts ('staging-' + [Guid]::NewGuid().ToString('N'))
$installer = Join-Path $staging 'installer'
$pluginDirectory = Join-Path $installer 'BepInEx/plugins/MultipurposeDrone'
$sourceDirectory = Join-Path $staging 'source'
New-Item -ItemType Directory -Force -Path $pluginDirectory, $sourceDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/bin/Release/netstandard2.1/Killdg.MultipurposeDrone.dll') -Destination $pluginDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $installer
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $pluginDirectory -Recurse -Force
$serverDirectory = Join-Path $installer 'SPT_Runtime/user/mods/MultipurposeDrone'
New-Item -ItemType Directory -Force -Path $serverDirectory | Out-Null
foreach ($file in @('items.json', 'assort.json'))
{
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "server/$file") -Destination $serverDirectory
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'server/bin/Release/net10.0/Killdg.MultipurposeDrone.Server.dll') -Destination $serverDirectory

# Explicit source manifest excludes game assemblies, decompiled references,
# build caches, local paths outside this project and unrelated project files.
$sourceManifest = @('README.md', '.editorconfig', 'Package.ps1')
foreach ($folder in @('src', 'tests', 'server', 'server-checks', 'assets'))
{
    $sourceManifest += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $folder) -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName) }
}
$sourceHashes = @{}
foreach ($relativePath in $sourceManifest)
{
    $original = Join-Path $PSScriptRoot $relativePath
    $destination = Join-Path $sourceDirectory $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $original -Destination $destination
    $sourceHashes[$relativePath] = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
}

$binary = Join-Path $pluginDirectory 'Killdg.MultipurposeDrone.dll'
$sourceCommit = git -C $PSScriptRoot rev-parse HEAD
$sourceChanges = git -C $PSScriptRoot status --porcelain
$sourceTag = git -C $PSScriptRoot tag --points-at HEAD --list 'v0.2.26'
$provenance = @{
    Version = '0.2.26'
    Author = 'killdg with ChatGPT assistance'
    SourceCommit = $sourceCommit
    SourceTag = $sourceTag
    SourceHasLocalChanges = [bool]$sourceChanges
    Verification = 'Build: zero warnings/errors; 65 battery/health/signal/native API and compiled regression checks plus native server JSON/offer checks; new behavior needs live validation'
    BinarySha256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
    SourceSha256 = $sourceHashes
    InstalledFiles = @(Get-ChildItem -LiteralPath $installer -Recurse -File | ForEach-Object {
        @{
            Path = [IO.Path]::GetRelativePath($installer, $_.FullName)
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
}
$provenance | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $artifacts 'Provenance.json') -Encoding utf8

Compress-Archive -Path (Join-Path $installer '*') -DestinationPath (Join-Path $artifacts 'Multipurpose-Drone-0.2.26-Beta.zip') -Force
Compress-Archive -Path (Join-Path $sourceDirectory '*') -DestinationPath (Join-Path $artifacts 'Multipurpose-Drone-0.2.26-Source.zip') -Force
Get-ChildItem -LiteralPath $artifacts -File |
    Where-Object { $_.Name -in @('Multipurpose-Drone-0.2.26-Beta.zip', 'Multipurpose-Drone-0.2.26-Source.zip', 'Provenance.json') } |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name } |
    Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding utf8
