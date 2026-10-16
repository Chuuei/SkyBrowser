# Run on Windows with: pwsh -STA -File tools/Build-ApplicationIcon.ps1
# The existing header geometry is the source of truth for the application mark.
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\src\SkyBrowser.Desktop\Assets\SkyBrowser.ico')
)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'WPF rendering requires STA. Run this script with pwsh -STA -File.'
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
[xml] $window = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\SkyBrowser.Desktop\MainWindow.xaml') -Raw
$namespaces = [Xml.XmlNamespaceManager]::new($window.NameTable)
$namespaces.AddNamespace('wpf', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$logoNode = $window.SelectSingleNode('//wpf:Grid[@Width="34" and @Height="34" and wpf:Ellipse[@Fill="#8BE2C3"]]', $namespaces)
if ($null -eq $logoNode) {
    throw 'Cannot find the header mark in MainWindow.xaml. Update the icon generator to match its geometry.'
}

# Discard only the spacing between the header mark and its text, not its shapes.
$logoNode.SetAttribute('Margin', '0')
$reader = [Xml.XmlNodeReader]::new($logoNode)
try {
    $logo = [Windows.Markup.XamlReader]::Load($reader)
}
finally {
    $reader.Dispose()
}
$logo.Measure([Windows.Size]::new(34, 34))
$logo.Arrange([Windows.Rect]::new(0, 0, 34, 34))
$logo.UpdateLayout()
$mark = [Windows.Media.VisualBrush]::new($logo)
$background = [Windows.Media.BrushConverter]::new().ConvertFromString('#101722')
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        # A dark tile keeps the mint outline legible on light and dark Windows themes.
        $scale = $size / 40.0
        $drawing.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
        $drawing.DrawRoundedRectangle($background, $null, [Windows.Rect]::new(0, 0, 40, 40), 7, 7)
        $drawing.DrawRectangle($mark, $null, [Windows.Rect]::new(3, 3, 34, 34))
        $drawing.Pop()
    }
    finally {
        $drawing.Close()
    }

    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        $frames.Add($stream.ToArray())
    }
    finally {
        $stream.Dispose()
    }
}

# ICO directories use little-endian entries; a zero size byte denotes 256 pixels.
# Each frame is an independently rendered PNG, preserving transparency at every DPI.
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
$output = [IO.File]::Create($OutputPath)
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] $sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte] $dimension)
        $writer.Write([byte] $dimension)
        $writer.Write([byte] 0)
        $writer.Write([byte] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] 32)
        $writer.Write([uint32] $frames[$index].Length)
        $writer.Write([uint32] $offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
}
Write-Output "Created $OutputPath with $($sizes.Count) icon sizes."
