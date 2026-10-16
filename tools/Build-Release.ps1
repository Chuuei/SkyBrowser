param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')] [string] $Version,
    [string] $OutputRoot = (Join-Path $PSScriptRoot '../artifacts/release')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $changes = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) {
        throw 'Release packaging requires committed source and a clean working tree, including the submodule.'
    }
    $output = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $Version
    $null = New-Item -ItemType Directory -Path $output -Force
    $work = Join-Path $output ('work-' + [Guid]::NewGuid().ToString('N'))
    $publish = Join-Path $work 'application'
    & dotnet publish src/SkyBrowser.Desktop -c Release -p:PublishProfile=WinX64 "-p:Version=$Version" --artifacts-path ./artifacts --output $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    & (Join-Path $PSScriptRoot 'Test-Publish.ps1') -PublishDirectory $publish
    $binaryZip = Join-Path $output "SkyBrowser-$Version-win-x64.zip"
    if (Test-Path -LiteralPath $binaryZip) { throw 'A release ZIP already exists; use a new version/output directory.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $binaryZip)

    $source = Join-Path $work 'source'
    $mainZip = Join-Path $work 'application-source.zip'
    $libraryZip = Join-Path $work 'library-source.zip'
    & git archive --format=zip "--output=$mainZip" HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Application source archive failed.' }
    & git -C dependencies/XISFLib.Net archive --format=zip "--output=$libraryZip" HEAD -- . ':(exclude)Examples' ':(exclude).idea'
    if ($LASTEXITCODE -ne 0) { throw 'Library source archive failed.' }
    [IO.Compression.ZipFile]::ExtractToDirectory($mainZip, $source)
    [IO.Compression.ZipFile]::ExtractToDirectory($libraryZip, (Join-Path $source 'dependencies/XISFLib.Net'), $true)
    [IO.Compression.ZipFile]::CreateFromDirectory($source, (Join-Path $output "SkyBrowser-$Version-source.zip"))
    & (Join-Path $root 'dependencies/XISFLib.Net/tools/Export-NativeSources.ps1') -OutputDirectory $output -CacheDirectory (Join-Path $root 'artifacts/third-party-sources')
    $checksums = Get-ChildItem -LiteralPath $output -Filter '*.zip' -File | Sort-Object Name | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
    Write-Output "Release assets prepared in $output"
} finally {
    Pop-Location
}
