# Renders the README header and the social preview from docs\brand\*.html with Microsoft Edge (it is on every
# Windows 10 and 11 PC). The product picture in them is the real screenshot docs\assets\screenshots\main-light.png,
# so take the screenshots first:
#
#   .\tools\screenshots.ps1
#   .\tools\brand.ps1
#
# The pages are plain HTML and CSS; open one in a browser to work on the design.
param(
    [string] $Out = "docs\assets"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$shot = Join-Path $root "docs\assets\screenshots\main-light.png"
if (-not (Test-Path $shot)) { throw "No main-light.png yet. Run .\tools\screenshots.ps1 first." }

$edge = @(
    (Join-Path ${env:ProgramFiles(x86)} "Microsoft\Edge\Application\msedge.exe"),
    (Join-Path $env:ProgramFiles "Microsoft\Edge\Application\msedge.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "Microsoft Edge was not found." }

# A profile of its own, so a running Edge is left alone and the render does not open in it.
$profile = Join-Path $env:TEMP "XamppControl\brand-profile"
$scale = 2

$jobs = @(
    @{ Page = "header.html"; Width = 1600; Height = 560; File = "header.png" },
    @{ Page = "social.html"; Width = 1280; Height = 640; File = "social-preview.png" }
)

Add-Type -AssemblyName System.Drawing
foreach ($job in $jobs) {
    $page = Join-Path $root "docs\brand\$($job.Page)"
    $target = Join-Path $root (Join-Path $Out $job.File)
    if (Test-Path $target) { Remove-Item $target }

    $arguments = @(
        "--headless", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
        "--force-device-scale-factor=$scale",
        "--user-data-dir=`"$profile`"",
        "--window-size=$($job.Width),$($job.Height)",
        "--screenshot=`"$target`"",
        "`"$(([System.Uri] $page).AbsoluteUri)`""
    )
    Start-Process -FilePath $edge -ArgumentList $arguments -Wait
    if (-not (Test-Path $target)) { throw "Edge wrote no $($job.File)." }

    # Edge versions differ in how they size a headless window; say so instead of committing a cropped picture.
    $image = [System.Drawing.Image]::FromFile($target)
    try { $size = "$($image.Width)x$($image.Height)" } finally { $image.Dispose() }
    $expected = "$($job.Width * $scale)x$($job.Height * $scale)"
    if ($size -eq $expected) {
        Write-Host "  $($job.File)  $size"
    }
    else {
        Write-Warning "$($job.File) is $size, expected $expected. Look at it before committing."
    }
}

Write-Host "Done. The social preview is uploaded by hand: repository Settings > Social preview."
