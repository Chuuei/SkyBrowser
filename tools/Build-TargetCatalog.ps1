param(
    [string] $SourceDirectory = (Join-Path $PSScriptRoot '..\artifacts\catalog-sources'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\src\SkyBrowser.Core\Data\deep-sky.json')
)

$ErrorActionPreference = 'Stop'
$commit = '4e2398a263de663d83f7b86be80c368c34335764'
$upstream = "https://raw.githubusercontent.com/mattiaverga/OpenNGC/$commit/database_files/"
$caldwellSource = 'https://www.astroleague.org/caldwell-program-object-list/'
New-Item -ItemType Directory -Path $SourceDirectory -Force | Out-Null
foreach ($name in @('NGC.csv', 'addendum.csv')) {
    $path = Join-Path $SourceDirectory $name
    if (!(Test-Path -LiteralPath $path)) {
        Invoke-WebRequest -Uri ($upstream + $name) -OutFile $path
    }
}

function Canonical([string] $name) {
    $value = ($name -replace '[\s_-]', '').ToUpperInvariant()
    $match = [regex]::Match($value, '^(NGC|IC|M|C|B|MEL)(\d+)(.*)$')
    if ($match.Success) {
        return $match.Groups[1].Value + [int]::Parse($match.Groups[2].Value) + $match.Groups[3].Value
    }
    return $value
}

function Number([string] $value) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }
    return [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

function Angle([string] $value, [bool] $rightAscension) {
    $parts = $value.TrimStart('+', '-') -split ':'
    if ($parts.Count -ne 3) {
        throw "Invalid coordinate: $value"
    }
    $angle = (Number $parts[0]) + (Number $parts[1]) / 60 + (Number $parts[2]) / 3600
    if ($value.StartsWith('-')) {
        $angle = -$angle
    }
    if ($rightAscension) {
        $angle *= 15
    }
    return [Math]::Round($angle, 9)
}

function Strings([string] $text) {
    return @(
        $text -split ',' |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ }
    )
}

function Add-Designation($target, [string] $name) {
    $id = Canonical $name
    if ($id -and !$target.Designations.Contains($id)) {
        $target.Designations.Add($id)
    }
}

$rows = @(Import-Csv -LiteralPath (Join-Path $SourceDirectory 'NGC.csv') -Delimiter ';') +
    @(Import-Csv -LiteralPath (Join-Path $SourceDirectory 'addendum.csv') -Delimiter ';')
$byId = @{}
$targets = @{}
foreach ($row in $rows) {
    $byId[(Canonical $row.Name)] = $row
}
foreach ($row in $rows) {
    if ($row.Type -in @('Dup', 'NonEx') -or !$row.RA -or !$row.Dec) {
        continue
    }
    $id = Canonical $row.Name
    $target = [ordered]@{
        Id = $id
        Designations = [Collections.Generic.List[string]]::new()
        CommonNames = @(Strings $row.'Common names')
        Position = [ordered]@{
            RightAscension = (Angle $row.RA $true)
            Declination = (Angle $row.Dec $false)
        }
        TypeCode = $row.Type
        Constellation = $row.Const
        MajorArcMinutes = (Number $row.MajAx)
        MinorArcMinutes = (Number $row.MinAx)
        PositionAngleDegrees = (Number $row.PosAng)
        Magnitude = $(
            if ($row.'V-Mag') {
                Number $row.'V-Mag'
            } else {
                Number $row.'B-Mag'
            }
        )
        MagnitudeBand = $(
            if ($row.'V-Mag') {
                'V'
            } elseif ($row.'B-Mag') {
                'B'
            } else {
                $null
            }
        )
        Note = $null
    }
    Add-Designation $target $id
    foreach ($m in (Strings $row.M)) {
        Add-Designation $target ('M' + $m)
    }
    $targets[$id] = $target
}
function Resolve([string] $id, [int] $depth = 0) {
    $id = Canonical $id
    if ($targets.ContainsKey($id)) {
        return $targets[$id]
    }
    if ($depth -gt 12 -or !$byId.ContainsKey($id)) {
        return $null
    }
    $row = $byId[$id]
    if ($row.Type -ne 'Dup') {
        return $null
    }
    foreach ($prefix in @('NGC', 'IC')) {
        foreach ($number in (Strings $row.$prefix)) {
            $parent = Resolve ($prefix + $number) ($depth + 1)
            if ($null -ne $parent) {
                return $parent
            }
        }
    }
    return $null
}

$unresolved = [Collections.Generic.List[string]]::new()
foreach ($row in $rows | Where-Object Type -eq 'Dup') {
    # Handled by the documented identification below.
    if ((Canonical $row.Name) -eq 'M102') {
        continue
    }
    $parent = Resolve $row.Name
    if ($null -ne $parent) {
        Add-Designation $parent $row.Name
        foreach ($m in (Strings $row.M)) {
            Add-Designation $parent ('M' + $m)
        }
    } else {
        $unresolved.Add($row.Name)
    }
}

# Add unambiguous cross-designations that do not have their own independent entry.
foreach ($row in $rows | Where-Object { $_.Type -notin @('Dup', 'NonEx') }) {
    $id = Canonical $row.Name
    if (!$targets.ContainsKey($id)) {
        continue
    }
    foreach ($prefix in @('NGC', 'IC')) {
        foreach ($number in (Strings $row.$prefix)) {
            $alias = Canonical ($prefix + $number)
            if (!$byId.ContainsKey($alias)) {
                Add-Designation $targets[$id] $alias
            }
        }
    }
}
# M102 is historically disputed. Use the NGC 5866 identification shown in NASA's Hubble Messier catalog.
Add-Designation $targets.NGC5866 'M102'
$targets.NGC5866.Note = 'M102 is identified with NGC 5866 here, following the Hubble Messier catalog; the historical identification is disputed.'
$targets.NGC5866.CommonNames = @('Spindle Galaxy')
# Correct the IC 434 common-name conflation in the snapshot. Horsehead = B33; Flame = NGC 2024.
# NASA: https://science.nasa.gov/asset/hubble/the-horsehead-nebula/
$targets.IC434.CommonNames = @('Horsehead background nebula', 'Orion B')
$targets.NGC253.CommonNames = @('Sculptor Galaxy', 'Silver Dollar Galaxy')

# Factual identifier cross-reference checked against the Astronomical League list on 2026-10-15.
# C14 is the combined Double Cluster; C33 and C49 use the principal nebular component.
# Coordinates and sizes remain from OpenNGC, including its standalone C9/C14/C41/C99 entries.
$caldwell = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'caldwell-identifiers.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($entry in $caldwell.GetEnumerator()) {
    $number = [int]$entry.Key
    $catalogId = $entry.Value
    $target = Resolve $catalogId
    if ($null -eq $target) {
        throw "Missing Caldwell $number target $catalogId"
    }
    Add-Designation $target ('C' + $number)
}
if ($caldwell.Count -ne 109) {
    throw "Expected 109 Caldwell entries, found $($caldwell.Count)."
}
$messier = @(
    $targets.Values.Designations |
        Where-Object { $_ -match '^M\d+$' } |
        Sort-Object -Unique
)
if ($messier.Count -ne 110) {
    throw "Expected 110 Messier identifiers, found $($messier.Count)."
}
$output = [ordered]@{
    Version = 1
    OpenNgcCommit = $commit
    License = 'CC-BY-SA-4.0'

    Attribution = 'Mattia Verga and OpenNGC contributors'
    Source = 'https://github.com/mattiaverga/OpenNGC'
    CaldwellReference = $caldwellSource
    Targets = @($targets.Values | Sort-Object Id)
}
$output | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
[ordered]@{
    Targets = $targets.Count
    Messier = $messier.Count
    Caldwell = $caldwell.Count

    UnresolvedDuplicateIds = $unresolved.ToArray()
    OutputBytes = (Get-Item -LiteralPath $OutputPath).Length
} | ConvertTo-Json -Depth 4
