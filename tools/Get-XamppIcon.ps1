<#
.SYNOPSIS
    Copies the icon from your XAMPP install into the project (Assets\xampp.png and Assets\xampp.ico).

.DESCRIPTION
    Extracts the icon embedded in xampp-control.exe at several sizes and writes:
      Assets\xampp.png  - largest size, used for the window header
      Assets\xampp.ico  - multi-size icon, used for the window and the exe
    Rebuild the project afterwards.

.EXAMPLE
    pwsh .\tools\Get-XamppIcon.ps1
    pwsh .\tools\Get-XamppIcon.ps1 -XamppRoot D:\xampp
#>
param(
    [string]$XamppRoot = 'C:\xampp'
)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $XamppRoot 'xampp-control.exe'
if (-not (Test-Path $exe)) {
    throw "Couldn't find $exe. Pass -XamppRoot if XAMPP lives elsewhere."
}

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class XamppIconExtractor
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy,
        IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr icon);
}
'@

$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$images = New-Object System.Collections.Generic.List[object]
foreach ($size in 256, 64, 48, 32, 16) {
    $handles = New-Object IntPtr[] 1
    $ids = New-Object uint32[] 1
    $count = [XamppIconExtractor]::PrivateExtractIcons($exe, 0, $size, $size, $handles, $ids, 1, 0)
    if ($count -lt 1 -or $handles[0] -eq [IntPtr]::Zero) { continue }

    $bitmap = [System.Drawing.Icon]::FromHandle($handles[0]).ToBitmap()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add([pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() })
    $bitmap.Dispose()
    [void][XamppIconExtractor]::DestroyIcon($handles[0])
}

if ($images.Count -eq 0) {
    throw "No icon found in $exe"
}

# Largest image as PNG for the header
[System.IO.File]::WriteAllBytes((Join-Path $assets 'xampp.png'), $images[0].Bytes)

# Multi-size .ico with PNG-compressed entries (supported since Windows Vista)
$icoPath = Join-Path $assets 'xampp.ico'
$file = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0)               # reserved
$writer.Write([uint16]1)               # type: icon
$writer.Write([uint16]$images.Count)   # image count
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }  # 0 means 256
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)             # palette
    $writer.Write([byte]0)             # reserved
    $writer.Write([uint16]1)           # color planes
    $writer.Write([uint16]32)          # bits per pixel
    $writer.Write([uint32]$image.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) {
    $writer.Write([byte[]]$image.Bytes)
}
$writer.Close()

Write-Host "Wrote $(Join-Path $assets 'xampp.png') and $icoPath ($($images.Count) sizes). Rebuild the project."
