# Builds the website into artifacts\site and serves it at http://localhost:8080 for a look before pushing.
#
#   .\tools\site.ps1            build and serve (Ctrl+C stops it)
#   .\tools\site.ps1 -NoServe   build only
#
# Needs Python 3.10 or newer (python.org or "winget install Python.Python.3.13"); the "markdown" package is
# installed on first use. The published site is built the same way by .github\workflows\pages.yml on every push
# to main, so this script is only for looking.
param(
    [switch] $NoServe,
    [int] $Port = 8080
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts\site"

$python = Get-Command python, py -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $python) { throw "Python was not found. Install it from python.org or with: winget install Python.Python.3.13" }

& $python.Source -c "import markdown" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing the markdown package..."
    & $python.Source -m pip install --quiet markdown
    if ($LASTEXITCODE -ne 0) { throw "Could not install the markdown package." }
}

& $python.Source (Join-Path $root "site\build.py") --out $out
if ($LASTEXITCODE -ne 0) { throw "The site did not build." }

if (-not $NoServe) {
    Write-Host "Serving http://localhost:$Port  (Ctrl+C to stop)"
    Start-Process "http://localhost:$Port"
    & $python.Source -m http.server $Port --directory $out
}
