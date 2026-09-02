$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$venvPython = Join-Path $projectRoot ".venv\Scripts\python.exe"

function Invoke-BootstrapPython {
    param([string[]]$Arguments)
    if ($env:AUTOELECTIVE_BOOTSTRAP_PYTHON) {
        & $env:AUTOELECTIVE_BOOTSTRAP_PYTHON @Arguments
        return
    }
    if (Get-Command py.exe -ErrorAction SilentlyContinue) {
        foreach ($candidate in @("3.12", "3.11", "3.10")) {
            & py.exe "-$candidate" -c "import sys" 2>$null
            if ($LASTEXITCODE -eq 0) {
                & py.exe "-$candidate" @Arguments
                return
            }
        }
    }
    if (Get-Command python.exe -ErrorAction SilentlyContinue) {
        & python.exe @Arguments
        return
    }
    throw "Python 3.10-3.12 was not found. Install Python 3.12 from python.org, then run install.cmd again."
}

Set-Location -LiteralPath $projectRoot
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Host "[1/4] Creating an isolated Python environment..."
    Invoke-BootstrapPython -Arguments @("-m", "venv", ".venv")
}

$version = & $venvPython -c "import sys; print('.'.join(map(str, sys.version_info[:2])))"
if ($version -notin @("3.10", "3.11", "3.12")) {
    throw "Python $version is not supported. Please use Python 3.10, 3.11 or 3.12."
}

Write-Host "[2/4] Updating pip..."
& $venvPython -m pip install --upgrade pip
Write-Host "[3/4] Installing local OCR and parser dependencies..."
& $venvPython -m pip install -r (Join-Path $projectRoot "requirements.txt")
& $venvPython -m compileall -q (Join-Path $projectRoot "engine")
Write-Host "[4/4] Building the Windows desktop application..."
& (Join-Path $projectRoot "build.cmd")
if ($LASTEXITCODE -ne 0) { throw "The desktop application build failed." }

Write-Host "Running offline self-tests..."
& $venvPython -m unittest discover -s (Join-Path $projectRoot "tests") -v
if ($LASTEXITCODE -ne 0) { throw "Offline self-tests failed." }
