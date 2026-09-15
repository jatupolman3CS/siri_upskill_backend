[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop', 'Status')][string]$Action = 'Start',
    [switch]$NoBuild,
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin'
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path $PSScriptRoot -Parent
$uiRoot = Join-Path (Split-Path $backendRoot -Parent) 'siri_upskill_ui'
$devRoot = Join-Path $backendRoot '.dev'
$logsRoot = Join-Path $devRoot 'logs'
$statePath = Join-Path $devRoot 'processes.json'
$settingsPath = Join-Path $devRoot 'settings.json'
$dotenvPath = Join-Path $backendRoot '.env'
$pgData = Join-Path $devRoot 'postgres'
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force $logsRoot | Out-Null
$processes = @()
if (Test-Path $statePath) {
    # Windows PowerShell 5.1 emits a JSON array as one pipeline object.
    $processes = Get-Content $statePath -Raw | ConvertFrom-Json
    $processes = @($processes)
}

function Save-State { [IO.File]::WriteAllText($statePath, (ConvertTo-Json -InputObject @($script:processes) -Depth 3), $utf8) }
function Get-OwnedProcess($entry) {
    $candidate = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
    if ($candidate -and $candidate.StartTime.ToUniversalTime().Ticks.ToString() -eq $entry.StartTicks) { return $candidate }
}
function Test-Port([int]$Port) {
    foreach ($address in @([Net.IPAddress]::Loopback, [Net.IPAddress]::IPv6Loopback)) {
        $client = [Net.Sockets.TcpClient]::new($address.AddressFamily)
        try {
            $task = $client.ConnectAsync($address, $Port)
            if ($task.Wait(300) -and $client.Connected) { return $true }
        } catch { } finally { $client.Dispose() }
    }
    return $false
}
function Wait-Port([int]$Port, [int]$Seconds = 60) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Port $Port) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Port $Port did not become ready. See $logsRoot."
}
function Start-DevProcess([string]$Name, [string]$File, [string]$Arguments, [string]$Directory) {
    $old = $script:processes | Where-Object Name -eq $Name | Select-Object -First 1
    if ($old -and (Get-OwnedProcess $old)) { return }
    $script:processes = @($script:processes | Where-Object Name -ne $Name)
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WorkingDirectory $Directory -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logsRoot "$Name.log") -RedirectStandardError (Join-Path $logsRoot "$Name.err.log")
    $script:processes += [pscustomobject]@{ Name = $Name; Id = $process.Id; StartTicks = $process.StartTime.ToUniversalTime().Ticks.ToString() }
    Save-State
}
function Stop-Dev {
    foreach ($name in @('stripe', 'ui', 'api', 'workers', 'mailpit', 'cache')) {
        foreach ($entry in @($script:processes | Where-Object Name -eq $name)) {
            $process = Get-OwnedProcess $entry
            if ($process) { Stop-Process -Id $process.Id -Force }
        }
    }
    if ((Test-Path (Join-Path $pgData 'postmaster.pid')) -and (Test-Path (Join-Path $PostgresBin 'pg_ctl.exe'))) {
        & (Join-Path $PostgresBin 'pg_ctl.exe') -D $pgData -m fast -w stop
        if ($LASTEXITCODE -ne 0) { throw 'Could not stop the development PostgreSQL cluster.' }
    }
    $script:processes = @()
    Save-State
    Write-Host 'Development processes stopped. Database and local email data retained.'
}
function Write-Status {
    foreach ($service in @(@('Web',4202), @('API',5190), @('PostgreSQL',5433), @('Cache (Garnet)',6380), @('Mailpit',8025))) {
        Write-Host ('{0,-18} {1,-8} localhost:{2}' -f $service[0], $(if (Test-Port $service[1]) {'Listening'} else {'Stopped'}), $service[1])
    }
    $worker = $script:processes | Where-Object Name -eq 'workers' | Select-Object -First 1
    Write-Host ('Workers            ' + $(if ($worker -and (Get-OwnedProcess $worker)) {'Running'} else {'Stopped'}))
    $stripe = $script:processes | Where-Object Name -eq 'stripe' | Select-Object -First 1
    Write-Host ('Stripe webhooks    ' + $(if ($stripe -and (Get-OwnedProcess $stripe)) {'Running (test mode)'} else {'Stopped / disabled'}))
}
if ($Action -eq 'Stop') { Stop-Dev; return }
if ($Action -eq 'Status') { Write-Status; return }

function New-LocalSecret {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); return [Convert]::ToBase64String($bytes) }
    finally { $rng.Dispose() }
}
function Read-Dotenv([string]$Path) {
    $values = @{}
    if (Test-Path $Path) {
        foreach ($line in [IO.File]::ReadAllLines($Path)) {
            if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
                $values[$matches[1]] = $matches[2].Trim().Trim('"').Trim("'")
            }
        }
    }
    return $values
}
function Install-LocalZip([string]$Name, [string]$Url, [string]$Hash, [string]$Executable) {
    $target = Join-Path $devRoot "tools\$Name"
    if (Test-Path (Join-Path $target $Executable)) { return }
    New-Item -ItemType Directory -Force $target | Out-Null
    $archive = Join-Path $devRoot "tools\$Name.zip"
    Write-Host "Downloading $Name for local development..."
    $previousProgress = $ProgressPreference
    try { $ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest -UseBasicParsing $Url -OutFile $archive }
    finally { $ProgressPreference = $previousProgress }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $Hash) { throw "$Name download checksum mismatch." }
    Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
}
function Run-Logged([string]$Name, [string]$File, [string[]]$Arguments, [string]$Directory) {
    Push-Location $Directory
    try {
        & $File @Arguments *> (Join-Path $logsRoot "$Name.log")
        if ($LASTEXITCODE -ne 0) { throw "$Name failed. See $(Join-Path $logsRoot "$Name.log")." }
    } finally { Pop-Location }
}

if (!(Test-Path (Join-Path $PostgresBin 'initdb.exe'))) { throw 'Install PostgreSQL 18, or pass -PostgresBin with its bin directory.' }
$dotnet = (Get-Command dotnet.exe).Source
$node = (Get-Command node.exe).Source
$npm = (Get-Command npm.cmd).Source
if (!(Test-Path (Join-Path $uiRoot 'package.json'))) { throw "Expected frontend repository at $uiRoot." }
# An occupied port can only be reused when it belongs to a process this script started.
foreach ($portOwner in @(@(4202,'ui'), @(5190,'api'), @(6380,'cache'), @(8025,'mailpit'), @(1025,'mailpit'))) {
    $entry = $processes | Where-Object Name -eq $portOwner[1] | Select-Object -First 1
    if ((Test-Port $portOwner[0]) -and !($entry -and (Get-OwnedProcess $entry))) {
        throw "Port $($portOwner[0]) is already in use by another process. It has not been stopped."
    }
}
if ((Test-Port 5433) -and !(Test-Path (Join-Path $pgData 'postmaster.pid'))) { throw 'Port 5433 is already in use by another PostgreSQL instance.' }
if (@($processes | Where-Object { Get-OwnedProcess $_ }).Count -gt 0) {
    Write-Host 'Development processes already exist. Use Stop before rebuilding/restarting.'
    Write-Status
    return
}

Install-LocalZip 'garnet' 'https://github.com/microsoft/garnet/releases/download/v2.1.5/win-x64-based-readytorun.zip' '7d1d40254ef11dbb12bf59c07b6543a04f2b51049f515cfc9745f556f96c7466' 'net10.0\GarnetServer.exe'
Install-LocalZip 'mailpit' 'https://github.com/axllent/mailpit/releases/download/v1.31.1/mailpit-windows-amd64.zip' '73ff05204741bd89cc96e6074573c4b22e1edfadf1b3427536233ff20c751604' 'mailpit.exe'
if (!(Test-Path $settingsPath)) {
    $settings = @{ AdminPassword = (New-LocalSecret); AppPassword = (New-LocalSecret) }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json), $utf8)
}
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
$connection = "Host=127.0.0.1;Port=5433;Database=SIRIUPSKILL;Username=siriupskill_dev;Password=$($settings.AppPassword)"
if (!(Test-Path $dotenvPath)) {
    $original = Read-Dotenv (Join-Path $backendRoot '.env')
    $stripeSecret = 'CHANGE_ME_DEV_ONLY_sk_test_placeholder_key'
    $stripePublic = 'CHANGE_ME_DEV_ONLY_pk_test_placeholder_key'
    $stripeWebhook = 'CHANGE_ME_DEV_ONLY_whsec_placeholder_key'
    if ([string]$original['Payment__Stripe__SecretKey'] -like 'sk_test_*') {
        $stripeSecret = $original['Payment__Stripe__SecretKey']
        $stripePublic = $original['Payment__Stripe__PublishableKey']
        $stripeWebhook = $original['Payment__Stripe__WebhookSecret']
    }
    $content = @"
# Native Windows development. Generated by scripts/dev.ps1; ignored by Git.
# Start API, workers, PostgreSQL, cache and Mailpit using that script.
SIRI_DEV_SAMPLE_VIDEO=true
SIRI_DEV_STRIPE_WEBHOOKS=false
ConnectionStrings__Default=$connection
Redis__ConnectionString=127.0.0.1:6380,abortConnect=false
ASPNETCORE_URLS=http://localhost:5190
Cors__AllowedOrigins__0=http://localhost:4202
Seo__PublicBaseUrl=http://localhost:4202
Identity__EmailConfirmation__ConfirmEmailUrl=http://localhost:4202/confirm-email
Identity__PasswordReset__ResetPasswordUrl=http://localhost:4202/reset-password
Identity__Jwt__SigningKey=$(New-LocalSecret)$(New-LocalSecret)
DataProtection__EncryptionKeyBase64=$(New-LocalSecret)
Identity__Seed__AdminEmail=admin@example.test
Identity__Seed__AdminPassword=LocalOnlyAdmin123!
Identity__Seed__TestUserPassword=LocalOnlyLearner123!
Email__Provider=Smtp
Email__Smtp__Host=127.0.0.1
Email__Smtp__Port=1025
Email__Smtp__Username=
Email__Smtp__Password=
Email__Smtp__FromAddress=no-reply@siriupskill.test
Email__Smtp__FromDisplayName=SIRI UpSkill Dev
Email__Smtp__UseStartTls=false
Email__Smtp__AllowInsecure=true
Payment__Stripe__SecretKey=$stripeSecret
Payment__Stripe__PublishableKey=$stripePublic
Payment__Stripe__WebhookSecret=$stripeWebhook
"@
    [IO.File]::WriteAllText($dotenvPath, $content + [Environment]::NewLine, $utf8)
}
$local = Read-Dotenv $dotenvPath
if ([string]$local['Payment__Stripe__SecretKey'] -like 'sk_live_*') { throw 'Use a Stripe test key in .env for this development stack.' }

# Child hosts inherit these local settings above any stale user-secrets/process values.
$local['ConnectionStrings__Default'] = $connection
$local['Redis__ConnectionString'] = '127.0.0.1:6380,abortConnect=false'
$local['Email__Provider'] = 'Smtp'
$local['Email__Smtp__Host'] = '127.0.0.1'
$local['Email__Smtp__Port'] = '1025'
$local['Email__Smtp__Username'] = ''
$local['Email__Smtp__Password'] = ''
$local['Email__Smtp__FromAddress'] = 'no-reply@siriupskill.test'
$local['Email__Smtp__FromDisplayName'] = 'SIRI UpSkill Dev'
$local['Email__Smtp__UseStartTls'] = 'false'
$local['Email__Smtp__AllowInsecure'] = 'true'
$local['SIRI_DEV_SAMPLE_VIDEO'] = 'true'
$local['ASPNETCORE_ENVIRONMENT'] = 'Development'
$local['DOTNET_ENVIRONMENT'] = 'Development'
$local['API_INTERNAL_URL'] = 'http://localhost:5190'
$local['PGPASSWORD'] = $settings.AdminPassword
$previousEnvironment = @{}
foreach ($key in $local.Keys) {
    $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    [Environment]::SetEnvironmentVariable($key, $local[$key], 'Process')
}
try {
    $stripeEnabled = $local['SIRI_DEV_STRIPE_WEBHOOKS'] -eq 'true'
    if ($stripeEnabled) {
        if ($local['Payment__Stripe__SecretKey'] -notlike 'sk_test_*') { throw 'Local Stripe forwarding requires a sk_test_ key.' }
        Install-LocalZip 'stripe' 'https://github.com/stripe/stripe-cli/releases/download/v1.50.10/stripe_1.50.10_windows_x86_64.zip' '6b7eba279496ff2c3c451d0ea9faaeb26cb70a402f025a8a87271ce57637eefe' 'stripe.exe'
        $stripeExe = Join-Path $devRoot 'tools\stripe\stripe.exe'
        $stripeConfig = Join-Path $devRoot 'stripe-config.toml'
        $priorStripeKey = $env:STRIPE_API_KEY
        try {
            $env:STRIPE_API_KEY = $local['Payment__Stripe__SecretKey']
            Run-Logged 'stripe-secret' $stripeExe @('listen','--print-secret','--skip-update','--config',$stripeConfig) $devRoot
        } finally { $env:STRIPE_API_KEY = $priorStripeKey }
        $signingSecret = [regex]::Match([IO.File]::ReadAllText((Join-Path $logsRoot 'stripe-secret.log')), 'whsec_[A-Za-z0-9]+').Value
        if (!$signingSecret) { throw 'Stripe did not return a local webhook signing secret.' }
        $local['Payment__Stripe__WebhookSecret'] = $signingSecret
        if (!$previousEnvironment.ContainsKey('Payment__Stripe__WebhookSecret')) {
            $previousEnvironment['Payment__Stripe__WebhookSecret'] = [Environment]::GetEnvironmentVariable('Payment__Stripe__WebhookSecret', 'Process')
        }
        [Environment]::SetEnvironmentVariable('Payment__Stripe__WebhookSecret', $signingSecret, 'Process')
        $secretLineFound = $false
        $dotenvLines = @(foreach ($line in [IO.File]::ReadAllLines($dotenvPath)) {
            if ($line -match '^\s*Payment__Stripe__WebhookSecret\s*=') {
                "Payment__Stripe__WebhookSecret=$signingSecret"
                $secretLineFound = $true
            } else { $line }
        })
        if (!$secretLineFound) { $dotenvLines += "Payment__Stripe__WebhookSecret=$signingSecret" }
        [IO.File]::WriteAllLines($dotenvPath, $dotenvLines, $utf8)
    }
    if (!(Test-Path (Join-Path $pgData 'PG_VERSION'))) {
        $passwordFile = Join-Path $devRoot 'initdb-password.tmp'
        [IO.File]::WriteAllText($passwordFile, $settings.AdminPassword, $utf8)
        try { Run-Logged 'initdb' (Join-Path $PostgresBin 'initdb.exe') @('-D',$pgData,'-U','siri_dev_admin','--auth=scram-sha-256',"--pwfile=$passwordFile",'--encoding=UTF8','--locale-provider=icu','--icu-locale=th-TH') $backendRoot }
        finally { Remove-Item -LiteralPath $passwordFile -ErrorAction SilentlyContinue }
        [IO.File]::AppendAllText((Join-Path $pgData 'postgresql.conf'), "`nlisten_addresses='127.0.0.1'`nport=5433`ntimezone='UTC'`n", $utf8)
    }
    if (!(Test-Port 5433)) {
        # Start-Process -Wait waits for descendants too, including the long-running database.
        # Wait only for pg_ctl itself; it already waits for PostgreSQL readiness with -w.
        $pgControl = Start-Process -FilePath (Join-Path $PostgresBin 'pg_ctl.exe') -ArgumentList "-D `"$pgData`" -l `"$logsRoot\postgres.log`" -w start" -WindowStyle Hidden -PassThru
        if (!$pgControl.WaitForExit(60000)) { throw 'PostgreSQL startup timed out.' }
        if ($pgControl.ExitCode -ne 0) { throw "PostgreSQL startup failed. See $logsRoot\postgres.log." }
    }
    $psql = Join-Path $PostgresBin 'psql.exe'
    $psqlArgs = @('-X','-h','127.0.0.1','-p','5433','-U','siri_dev_admin','-d','postgres','-v','ON_ERROR_STOP=1')
    $exists = & $psql @psqlArgs -Atc "SELECT 1 FROM pg_roles WHERE rolname='siriupskill_dev'"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot connect to the native development cluster.' }
    if ($exists -ne '1') {
        "CREATE ROLE siriupskill_dev LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '$($settings.AppPassword)';" | & $psql @psqlArgs *> (Join-Path $logsRoot 'create-role.log')
        if ($LASTEXITCODE -ne 0) { throw 'Development database role creation failed.' }
    }
    $exists = & $psql @psqlArgs -Atc "SELECT 1 FROM pg_database WHERE datname='SIRIUPSKILL'"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot query development databases.' }
    if ($exists -ne '1') { Run-Logged 'create-database' (Join-Path $PostgresBin 'createdb.exe') @('-h','127.0.0.1','-p','5433','-U','siri_dev_admin','-O','siriupskill_dev','SIRIUPSKILL') $backendRoot }

    if (!$NoBuild) { Write-Host 'Building .NET...'; Run-Logged 'build' $dotnet @('build','SiriUpSkill.sln','--nologo') $backendRoot }
    if (!(Test-Path (Join-Path $uiRoot 'node_modules\@angular\cli\bin\ng.js'))) { Write-Host 'Installing frontend dependencies...'; Run-Logged 'npm-ci' $npm @('ci') $uiRoot }
    Write-Host 'Applying local migrations and sample data...'
    $apiRoot = Join-Path $backendRoot 'src\Siri.Api'
    $workerRoot = Join-Path $backendRoot 'src\Siri.Workers'
    Run-Logged 'migrate' $dotnet @('bin/Debug/net10.0/Siri.Api.dll','--environment','Development','--migrate') $apiRoot
    Run-Logged 'seed' $dotnet @('bin/Debug/net10.0/Siri.Api.dll','--environment','Development','--seed') $apiRoot
    # Switch only the two known sample assets created by MediaSeeder in this local database.
    # Uploaded media references are outside this predicate and retain their original provider.
    $sampleVideoEnabled = !$local.ContainsKey('SIRI_DEV_SAMPLE_VIDEO') -or $local['SIRI_DEV_SAMPLE_VIDEO'] -eq 'true'
    $targetSample = if ($sampleVideoEnabled) { 'mock-video-demo-1' } else { '448944e4-c1bd-4f61-a8b1-3e10e469aa69' }
    $sampleSql = @'
UPDATE "CATALOG"."COURSE_EPISODES" AS episode
SET "MEDIA_ASSET_ID" = target."MEDIA_ASSET_ID"
FROM "MEDIA"."MEDIA_ASSETS" AS target
WHERE target."PROVIDER" = 'BunnyStream' AND target."PROVIDER_ASSET_ID" = '__DEV_SAMPLE_TARGET__'
  AND episode."MEDIA_ASSET_ID" IN (
    SELECT "MEDIA_ASSET_ID" FROM "MEDIA"."MEDIA_ASSETS"
    WHERE "PROVIDER" = 'BunnyStream'
      AND "PROVIDER_ASSET_ID" IN ('mock-video-demo-1', '448944e4-c1bd-4f61-a8b1-3e10e469aa69'))
  AND episode."MEDIA_ASSET_ID" <> target."MEDIA_ASSET_ID";
'@
    $sampleSql.Replace('__DEV_SAMPLE_TARGET__', $targetSample) | & $psql -X -h 127.0.0.1 -p 5433 -U siri_dev_admin -d SIRIUPSKILL -v ON_ERROR_STOP=1 *> (Join-Path $logsRoot 'sample-video.log')
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the local sample video.' }
    Write-Host $(if ($sampleVideoEnabled) { 'Sample courses use the public HLS test clip (development only).' } else { 'Sample courses use the configured Bunny library.' })
    Start-DevProcess 'cache' (Join-Path $devRoot 'tools\garnet\net10.0\GarnetServer.exe') '--bind 127.0.0.1 --port 6380 --memory 128m --page 4m --index 8m' $devRoot
    Start-DevProcess 'mailpit' (Join-Path $devRoot 'tools\mailpit\mailpit.exe') "--listen 127.0.0.1:8025 --smtp 127.0.0.1:1025 --database `"$devRoot\mailpit.db`" --disable-version-check --smtp-disable-rdns" $devRoot
    Wait-Port 6380
    Wait-Port 1025
    Start-DevProcess 'api' $dotnet 'bin/Debug/net10.0/Siri.Api.dll --environment Development --urls http://localhost:5190 --Email:Smtp:Username= --Email:Smtp:Password=' $apiRoot
    Wait-Port 5190
    Start-DevProcess 'workers' $dotnet 'bin/Debug/net10.0/Siri.Workers.dll --environment Development --Email:Smtp:Username= --Email:Smtp:Password=' $workerRoot
    Start-DevProcess 'ui' $node 'node_modules/@angular/cli/bin/ng.js serve --host localhost --port 4202' $uiRoot
    Wait-Port 4202 120
    $health = Invoke-WebRequest -UseBasicParsing 'http://localhost:5190/health' -TimeoutSec 15
    if ($health.StatusCode -ne 200) { throw 'API health check failed.' }
    $worker = $script:processes | Where-Object Name -eq 'workers' | Select-Object -First 1
    if (!(Get-OwnedProcess $worker)) { throw "Workers exited. See $logsRoot\workers.log." }
    if ($stripeEnabled) {
        $priorStripeKey = $env:STRIPE_API_KEY
        try {
            $env:STRIPE_API_KEY = $local['Payment__Stripe__SecretKey']
            Start-DevProcess 'stripe' $stripeExe "listen --skip-update --config `"$stripeConfig`" --events payment_intent.succeeded,payment_intent.payment_failed,payment_intent.canceled --forward-to http://localhost:5190/api/commerce/webhooks/stripe" $devRoot
        } finally { $env:STRIPE_API_KEY = $priorStripeKey }
        $stripeDeadline = [DateTime]::UtcNow.AddSeconds(30)
        $stripeReady = $false
        while ([DateTime]::UtcNow -lt $stripeDeadline) {
            # Get-Content permits concurrent writers; File.ReadAllText conflicts with redirected output on Windows.
            $stripeOutput = (Get-Content -LiteralPath (Join-Path $logsRoot 'stripe.log') -Raw) + (Get-Content -LiteralPath (Join-Path $logsRoot 'stripe.err.log') -Raw)
            if ($stripeOutput -match 'Ready!') { $stripeReady = $true; break }
            Start-Sleep -Milliseconds 500
        }
        if (!$stripeReady) { throw "Stripe listener did not become ready. See $logsRoot\stripe.err.log." }
    }
    Write-Status
    Write-Host "`nOpen http://localhost:4202 | API http://localhost:5190/swagger | Email http://localhost:8025"
    Write-Host 'Learner: learner1.seed@example.test / LocalOnlyLearner123!'
    Write-Host 'Admin: admin@example.test / LocalOnlyAdmin123!'
    Write-Host "Logs: $logsRoot"
} catch {
    Write-Warning 'Development startup failed. Stopping processes owned by this script; data is retained.'
    Stop-Dev
    throw
} finally {
    foreach ($key in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process') }
}
