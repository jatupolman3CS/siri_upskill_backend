<#
.SYNOPSIS
    Runs k6 load testing suite for SiriUpSkill platform (P7-01).

.PARAMETER Scenario
    The load test scenario to run: "viewer", "checkout", "search", or "all". Default is "search".

.PARAMETER BaseUrl
    Base URL of the target API server. Default is "http://localhost:5190".
#>
param(
    [ValidateSet("viewer", "checkout", "search", "all")]
    [string]$Scenario = "search",

    [string]$BaseUrl = "http://localhost:5190"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       SiriUpSkill Performance & Load Testing Runner      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target Base URL: $BaseUrl" -ForegroundColor Yellow
Write-Host "Scenario:        $Scenario" -ForegroundColor Yellow
Write-Host ""

# Verify k6 installation
$k6Installed = Get-Command k6 -ErrorAction SilentlyContinue
if (-not $k6Installed) {
    Write-Warning "k6 executable was not found in PATH."
    Write-Host "To install k6 on Windows: `winget install k6` or `choco install k6`" -ForegroundColor Yellow
    Write-Host "Alternatively, download the binary from https://k6.io/docs/get-started/installation/" -ForegroundColor Yellow
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Run-K6Test {
    param([string]$ScriptFile, [string]$Name)
    Write-Host ">>> Running scenario: $Name ($ScriptFile)" -ForegroundColor Green
    $env:BASE_URL = $BaseUrl
    & k6 run (Join-Path $scriptDir $ScriptFile)
    if ($LASTEXITCODE -ne 0) {
        Write-Error "k6 test failed for scenario: $Name"
    }
}

switch ($Scenario) {
    "search" {
        Run-K6Test "search-burst.js" "Search Burst (200 req/sec)"
    }
    "checkout" {
        Run-K6Test "checkout-burst.js" "Checkout Flash Burst (500 checkouts/min)"
    }
    "viewer" {
        Run-K6Test "viewer-stream.js" "Viewer Stream (5,000 concurrent viewers)"
    }
    "all" {
        Run-K6Test "search-burst.js" "Search Burst"
        Run-K6Test "checkout-burst.js" "Checkout Flash Burst"
        Run-K6Test "viewer-stream.js" "Viewer Stream"
    }
}

Write-Host ""
Write-Host "Load test run completed successfully." -ForegroundColor Green
