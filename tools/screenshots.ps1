# Renders the screenshots with the app itself: every scene is the real window, posed on the pretend PC
# (Core/MockMachine.cs), captured off the screen with its title bar.
#
#   .\tools\screenshots.ps1                    the 12 showcase scenes the README shows (light theme),
#                                              into docs\assets\screenshots
#   .\tools\screenshots.ps1 -Set all           all 42 scenes, into artifacts\screenshots (not committed)
#   .\tools\screenshots.ps1 main,netstat       just those scenes
#   .\tools\screenshots.ps1 versions*          every scene whose name matches (wildcards)
#   .\tools\screenshots.ps1 -Themes dark       the dark theme (or light,dark for both)
#   .\tools\screenshots.ps1 -Lang nb           in another language from Lang\
#   .\tools\screenshots.ps1 -Size 1200x860     the main window's client size (default: the app's own)
#
# Windows only (the capture is GDI). Nothing on this PC is started, stopped or changed. Keep the mouse and other
# windows away from the middle of the screen while it runs; the screen's scaling decides the pixel size of the
# PNGs, 125% or 150% gives the crisp ones GitHub shows well.
param(
    [string[]] $Scenes = @(),
    [ValidateSet("showcase", "all")]
    [string] $Set = "showcase",
    [string[]] $Themes = @("light"),
    [string] $Lang = "en",
    [string] $Size = "",
    [string] $Out = "",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "XamppControl.csproj"

# The showcase set is what the README shows and is committed; the full set is for looking, not for the repo.
if (-not $Out) { $Out = if ($Set -eq "all") { "artifacts\screenshots" } else { "docs\assets\screenshots" } }
$outFolder = Join-Path $root $Out

Write-Host "Building $Configuration..."
dotnet build $project -c $Configuration --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$exe = Get-ChildItem (Join-Path $root "bin\$Configuration") -Recurse -Filter "XamppControl.exe" |
    Where-Object { $_.FullName -notmatch "\\publish\\" } | Select-Object -First 1
if (-not $exe) { throw "XamppControl.exe not found under bin\$Configuration." }

# Every scene, in the order of Views/MainWindow.Showcase.cs. Wildcards pick from it: versions*, *-conflict.
$known = @(
    "main", "stopped", "starting", "services", "new-versions", "update", "config-error", "menu", "quit", "no-xampp",
    "xampp-missing", "port-conflict", "netstat", "netstat-xampp", "netstat-filter", "netstat-conflict",
    "versions", "versions-mariadb", "versions-tools", "versions-installing", "versions-switched",
    "sites", "sites-untrusted", "sites-empty", "php", "php-no-xdebug", "php-xdebug-update",
    "databases", "databases-drop", "databases-stopped", "logs", "logs-filter", "logs-php",
    "config", "config-language", "ports", "user-files", "log-options",
    "about", "whats-new", "whats-new-installing", "crash")

# The scenes the README's tour is built from: one per feature worth showing, in the README's order.
$showcase = @(
    "main", "new-versions", "versions", "versions-mariadb", "port-conflict", "netstat", "config-error",
    "sites", "php", "databases", "logs", "config")
if (-not $Scenes) { $Scenes = if ($Set -eq "all") { $known } else { $showcase } }
$Scenes = @($Scenes | ForEach-Object {
        $pattern = $_
        if ($pattern -match "[*?]") { $known | Where-Object { $_ -like $pattern } } else { $pattern }
    } | Select-Object -Unique)
if (-not $Scenes) { throw "No scene matches." }
$unknown = @($Scenes | Where-Object { $_ -notin $known })
if ($unknown) { throw "Unknown scene: $($unknown -join ', ')" }

New-Item -ItemType Directory -Force -Path $outFolder | Out-Null
foreach ($theme in $Themes) {
    foreach ($scene in $Scenes) {
        $file = "$scene-$theme.png"
        $shot = Join-Path $outFolder $file
        Remove-Item $shot, "$shot.error.txt" -ErrorAction SilentlyContinue
        Write-Host "  $scene ($theme) -> $Out\$file"

        $arguments = @("--scene", $scene, "--shot", "`"$shot`"", "--theme", $theme, "--lang", $Lang)
        if ($Size) { $arguments += @("--size", $Size) }
        $process = Start-Process -FilePath $exe.FullName -ArgumentList $arguments -PassThru
        if (-not $process.WaitForExit(60000)) {
            $process.Kill()
            Write-Warning "  $scene ($theme) did not finish within a minute and was closed."
        }

        if (Test-Path "$shot.error.txt") { Write-Warning ("  " + (Get-Content "$shot.error.txt" -TotalCount 1)) }
        elseif (-not (Test-Path $shot)) { Write-Warning "  nothing written for $scene ($theme)" }
    }
}
Write-Host "Done."
