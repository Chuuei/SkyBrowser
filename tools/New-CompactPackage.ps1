param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $DependencyManifest
)

$ErrorActionPreference = 'Stop'
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$validation = @{ PublishDirectory = $PublishDirectory }
if ($DependencyManifest) {
    $validation.DependencyManifest = $DependencyManifest
}
& (Join-Path $PSScriptRoot 'Test-Publish.ps1') @validation
if (Test-Path -LiteralPath $OutputPath) {
    throw 'The compact archive already exists; choose a new output path.'
}

# Runtime libraries are already inside the standard self-contained executable.
# Only documentation, notices and the independently redistributable catalogue
# may be moved into the companion archive. Never hide an external runtime DLL.
$files = @(Get-ChildItem -LiteralPath $PublishDirectory -File -Recurse)
$supplements = @()
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($PublishDirectory, $file.FullName).Replace('\', '/')
    if ($relative -in @('SkyBrowser.exe', 'SkyBrowser-User-Manual.pdf')) {
        continue
    }
    if ($relative -notin @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md') -and
        $relative -notmatch '^Data/.+\.(txt|md|json)$') {
        throw "Compact packaging cannot relocate a possible runtime dependency: $relative"
    }
    $supplements += @{ Source = $file.FullName; Relative = $relative }
}

$parent = Split-Path $OutputPath -Parent
$work = Join-Path $parent ('compact-work-' + [Guid]::NewGuid().ToString('N'))
$application = Join-Path $work 'application'
$notices = Join-Path $work 'notices'
$null = New-Item -ItemType Directory -Path $application,$notices -Force
foreach ($item in $supplements) {
    $destination = Join-Path $notices $item.Relative
    $null = New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force
    Copy-Item -LiteralPath $item.Source -Destination $destination
}
foreach ($name in @('SkyBrowser.exe', 'SkyBrowser-User-Manual.pdf')) {
    Copy-Item -LiteralPath (Join-Path $PublishDirectory $name) -Destination $application
}
[IO.Compression.ZipFile]::CreateFromDirectory($notices, (Join-Path $application 'SkyBrowser-Notices.zip'))
[IO.Compression.ZipFile]::CreateFromDirectory($application, $OutputPath)

# Validate bytes from the finished download, including its nested notices ZIP.
# This protects catalogue/licence completeness as well as the runnable binary.
$archive = [IO.Compression.ZipFile]::OpenRead($OutputPath)
try {
    $expected = @('SkyBrowser.exe', 'SkyBrowser-User-Manual.pdf', 'SkyBrowser-Notices.zip')
    if ($archive.Entries.Count -ne 3 -or @($archive.Entries | Where-Object FullName -notin $expected).Count -ne 0) {
        throw 'The compact archive must contain exactly the executable, manual and notices archive.'
    }
    function Assert-ArchivedFile($entry, [string] $source) {
        if ($null -eq $entry) { throw "Missing archived file: $source" }
        $stream = $entry.Open()
        try {
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
            if ($hash -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
                throw "Archived bytes differ from the validated publish: $($entry.FullName)"
            }
        } finally {
            $stream.Dispose()
        }
    }
    foreach ($name in @('SkyBrowser.exe', 'SkyBrowser-User-Manual.pdf')) {
        Assert-ArchivedFile $archive.GetEntry($name) (Join-Path $PublishDirectory $name)
    }
    $stream = $archive.GetEntry('SkyBrowser-Notices.zip').Open()
    $buffer = [IO.MemoryStream]::new()
    try {
        $stream.CopyTo($buffer)
        $buffer.Position = 0
        $inner = [IO.Compression.ZipArchive]::new($buffer, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            if ($inner.Entries.Count -ne $supplements.Count) { throw 'The companion archive has an unexpected file count.' }
            foreach ($item in $supplements) {
                Assert-ArchivedFile $inner.GetEntry($item.Relative) $item.Source
            }
        } finally {
            $inner.Dispose()
        }
    } finally {
        $stream.Dispose()
        $buffer.Dispose()
    }
} finally {
    $archive.Dispose()
}
Write-Output "Compact package validated: 3 distribution files; $($supplements.Count) supplementary files preserved."
Write-Output $OutputPath
