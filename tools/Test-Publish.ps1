param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [string] $DependencyManifest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$required = @(
    'SkyBrowser.exe', 'SkyBrowser-User-Manual.pdf', 'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md',
    'Data/deep-sky.json', 'Data/LICENSE-OpenNGC.txt', 'Data/LICENSE-OpenNGC-ATTRIBUTION.txt',
    'Data/LICENSE-Natural-Earth.txt', 'Data/LICENSE-d3-celestial.txt', 'Data/LICENSE-Zstandard.txt', 'Data/LICENSE-LZ4.txt',
    'Data/DotNet-THIRD-PARTY-NOTICES.txt', 'Data/WindowsDesktop-THIRD-PARTY-NOTICES.txt',
    'Data/XISFLib-LICENSE.txt', 'Data/XISFLib-NOTICE.txt', 'Data/XISFLib-THIRD-PARTY-NOTICES.md',
    'Data/LICENSE-DSS-SURVEYS.txt', 'Data/LICENSE-NASA-DISTANCES.txt', 'Data/LICENSE-SIMBAD-DISTANCES.txt'
)
$licenseInventory = Get-Content (Join-Path $PSScriptRoot 'third-party-packages.json') -Raw | ConvertFrom-Json -AsHashtable
$inventory = $licenseInventory['packages']
# The generated runtime dependency graph excludes SDK build tools that are not
# redistributed. Auditing their versions as runtime code would tie releases to one SDK.
$depsPath = if ($DependencyManifest) { [IO.Path]::GetFullPath($DependencyManifest) } else {
    Join-Path $root 'artifacts/bin/SkyBrowser.Desktop/release_win-x64/SkyBrowser.deps.json'
}
$deps = Get-Content $depsPath -Raw | ConvertFrom-Json -AsHashtable
$packages = @($deps['libraries'].GetEnumerator() | Where-Object { $_.Value['type'] -eq 'package' } | ForEach-Object Key)
$runtimePacks = @($deps['libraries'].GetEnumerator() | Where-Object { $_.Value['type'] -eq 'runtimepack' })
if ($runtimePacks.Count -lt 2 -or @($runtimePacks | Where-Object { ($_.Key -split '/')[-1] -ne $licenseInventory['runtimeVersion'] }).Count -gt 0) {
    throw 'The self-contained runtime does not match the reviewed runtime version and notices.'
}
$unknown = @($packages | Where-Object { -not $inventory.ContainsKey($_) })
if ($unknown.Count -gt 0) {
    throw "New/unreviewed runtime packages: $($unknown -join ', '). Update licenses and the inventory."
}
foreach ($package in $packages) {
    $required += 'Data/' + $inventory[$package]
}
foreach ($path in $required | Select-Object -Unique) {
    $fullPath = Join-Path $PublishDirectory $path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf) -or (Get-Item -LiteralPath $fullPath).Length -eq 0) {
        throw "Missing release file: $path"
    }
}
$version = & (Join-Path $PSScriptRoot 'Get-Version.ps1')
$executable = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishDirectory 'SkyBrowser.exe'))
if (($executable.ProductVersion -split '\+', 2)[0] -cne $version) {
    throw "The published executable's product version does not match Version.props ('$version')."
}
$numericVersion = [System.Management.Automation.SemanticVersion]::Parse($version)
if ($executable.FileVersion -ne "$($numericVersion.Major).$($numericVersion.Minor).$($numericVersion.Patch).0") {
    throw 'The published executable has inconsistent Windows file version metadata.'
}
$forbidden = @(Get-ChildItem -LiteralPath $PublishDirectory -File -Recurse | Where-Object {
    $_.Extension -match '^\.(pdb|xml|fits|fit|fts|xisf|xish|xisb|pyc)$' -or
    $_.Name -match '(?i)settings\.json|\.sky\.json$|\.sqlite3?$'
})
if ($forbidden.Count -gt 0) {
    $extensions = ($forbidden.Extension | Sort-Object -Unique) -join ', '
    throw "Private/scientific/debug/XML files were found in the release package (extensions: $extensions)."
}
$nativeLock = Get-Content (Join-Path $root 'dependencies/XISFLib.Net/tools/native-source-lock.json') -Raw | ConvertFrom-Json
if ('Magick.NET-Q16-AnyCPU/' + $nativeLock.magickNetVersion -notin $packages) {
    throw 'The native source lock does not match the runtime package version.'
}
Write-Output "Publish package validated: $($packages.Count) reviewed packages, manual, notices and catalogue data."
