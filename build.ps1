param(
    [string]$SPTPath = 'B:/SPT MOD TESTING',
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$catalogs = @(Get-ChildItem -LiteralPath "$PSScriptRoot/Locales" -Filter '*.json')
if ($catalogs.Count -ne 16) {
    throw 'Expected 16 locale catalogs.'
}

$keys = $null
foreach ($catalog in $catalogs) {
    $entries = Get-Content -LiteralPath $catalog.FullName -Raw | ConvertFrom-Json -AsHashtable
    if ($entries.Count -ne 16 -or @($entries.Values | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count) {
        throw "Incomplete catalog: $($catalog.Name)"
    }
    if ($null -eq $keys) {
        $keys = @($entries.Keys)
    }
    if (Compare-Object -ReferenceObject $keys -DifferenceObject @($entries.Keys) -CaseSensitive) {
        throw "Mismatched keys: $($catalog.Name)"
    }
}

$artifacts = Join-Path $PSScriptRoot 'artifacts'
$package = Join-Path $artifacts ('staging-' + [guid]::NewGuid().ToString('N'))
$destinations = @{
    Client = 'BepInEx/plugins/MultipurposeDroneLanguages'
    Server = 'SPT_Runtime/user/mods/MultipurposeDroneLanguages'
}

foreach ($side in @('Client', 'Server')) {
    $project = Join-Path $PSScriptRoot "src/$side/LanguagePack.csproj"
    & $Dotnet build $project -c Release "-p:SPTPath=$SPTPath" "-p:SPTBinaries=$SPTPath/SPT_Runtime" --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "$side build failed."
    }

    $destination = Join-Path $package $destinations[$side]
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $assembly = if ($side -eq 'Client') {
        'netstandard2.1/MultipurposeDrone.LanguagePack.Client.dll'
    } else {
        'net10.0/MultipurposeDrone.LanguagePack.dll'
    }
    Copy-Item -LiteralPath "$PSScriptRoot/src/$side/bin/Release/$assembly" -Destination $destination
    Copy-Item -LiteralPath "$PSScriptRoot/Locales" -Destination $destination -Recurse
}

Copy-Item -LiteralPath "$PSScriptRoot/README.md" -Destination $package
$zip = Join-Path $artifacts 'Multipurpose-Drone-Language-Pack-1.0.0.zip'
Compress-Archive -Path "$package/*" -DestinationPath $zip -Force
Get-FileHash -LiteralPath $zip
