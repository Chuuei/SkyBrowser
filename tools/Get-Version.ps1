param([string] $ExpectedVersion)

$ErrorActionPreference = 'Stop'
$versionPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Version.props'
$version = [string]([xml](Get-Content -LiteralPath $versionPath -Raw)).Project.PropertyGroup.Version
try {
    $parsed = [System.Management.Automation.SemanticVersion]::Parse($version)
} catch {
    throw 'Version.props must contain a valid semantic version, such as 0.2.0 or 0.2.0-rc.1.'
}
if ($parsed.ToString() -cne $version) {
    throw 'Version.props must use the complete major.minor.patch form, without leading zeroes.'
}
if ($parsed.BuildLabel) {
    throw 'Leave build metadata out of Version.props; the .NET SDK adds the Git commit when available.'
}
if ($ExpectedVersion -and $ExpectedVersion -cne $version) {
    throw "Release version '$ExpectedVersion' does not match Version.props ('$version'). Update and commit Version.props before tagging."
}
$version
