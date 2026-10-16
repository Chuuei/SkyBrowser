param([Parameter(Mandatory)] [string] $PublishDirectory)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$required = @(
    'SkyBrowser.exe', 'SkyBrowser-User-Manual.pdf', 'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md',
    'Data/deep-sky.json', 'Data/LICENSE-OpenNGC.txt', 'Data/LICENSE-OpenNGC-ATTRIBUTION.txt',
    'Data/LICENSE-Natural-Earth.txt', 'Data/LICENSE-d3-celestial.txt', 'Data/LICENSE-Zstandard.txt',
    'Data/DotNet-THIRD-PARTY-NOTICES.txt', 'Data/WindowsDesktop-THIRD-PARTY-NOTICES.txt',
    'Data/XISFLib-LICENSE.txt', 'Data/XISFLib-NOTICE.txt', 'Data/XISFLib-THIRD-PARTY-NOTICES.md'
)
$inventory = Get-Content (Join-Path $PSScriptRoot 'third-party-packages.json') -Raw | ConvertFrom-Json -AsHashtable
$assetsPath = Join-Path $root 'artifacts/obj/SkyBrowser.Desktop/project.assets.json'
$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$packages = @($assets['libraries'].GetEnumerator() | Where-Object { $_.Value['type'] -eq 'package' } | ForEach-Object Key)
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
