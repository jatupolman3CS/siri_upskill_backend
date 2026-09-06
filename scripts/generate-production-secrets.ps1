<#
.SYNOPSIS
    Generates cryptographically secure production keys and secrets for SiriUpSkill platform (P7-12).

.PARAMETER OutputEnvFile
    Path to write the generated .env.production file. Default is ".env.production".
#>
param(
    [string]$OutputEnvFile = ".env.production"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     SiriUpSkill Production Secret Generator (P7-12)      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Generate 32-byte Base64 AES-256 Data Protection Key
$aesBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($aesBytes)
$dataProtectionKey = [Convert]::ToBase64String($aesBytes)

# 2. Generate 64-character JWT Signing Key
$jwtBytes = New-Object byte[] 48
$rng.GetBytes($jwtBytes)
$jwtSigningKey = [Convert]::ToBase64String($jwtBytes)

# 3. Generate Strong Database Password (24 chars)
$dbBytes = New-Object byte[] 18
$rng.GetBytes($dbBytes)
$dbPassword = "Siri_" + [Convert]::ToBase64String($dbBytes).Replace("/", "_").Replace("+", "-") + "!"

# 4. Generate Strong Redis Password
$redisBytes = New-Object byte[] 24
$rng.GetBytes($redisBytes)
$redisPassword = [Convert]::ToBase64String($redisBytes).Replace("/", "").Replace("+", "")

$envContent = @"
# ==============================================================================
# SiriUpSkill Production Environment Configuration
# Generated on: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC")
# CAUTION: NEVER COMMIT THIS FILE TO VERSION CONTROL
# ==============================================================================

# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000

# Database (PostgreSQL 17 — task P0-41)
POSTGRES_PASSWORD=$dbPassword
ConnectionStrings__Default=Host=postgres;Port=5432;Database=SIRIUPSKILL;Username=siriupskill_app;Password=$dbPassword;Maximum Pool Size=200

# Redis
REDIS_PASSWORD=$redisPassword
ConnectionStrings__Redis=redis:6379,password=$redisPassword,ssl=false,abortConnect=false,connectTimeout=5000

# Data Protection (AES-256 32-byte key)
DataProtection__EncryptionKeyBase64=$dataProtectionKey

# Identity & JWT
Identity__Jwt__SigningKey=$jwtSigningKey
Identity__Jwt__Issuer=https://api.siriupskill.com
Identity__Jwt__Audience=https://siriupskill.com
Identity__Jwt__AccessTokenLifetimeMinutes=15
Identity__Jwt__RefreshTokenLifetimeDays=30

# Stripe Payments (Fill with live credentials)
Stripe__SecretKey=sk_live_REPLACE_WITH_YOUR_STRIPE_LIVE_KEY
Stripe__PublishableKey=pk_live_REPLACE_WITH_YOUR_STRIPE_LIVE_KEY
Stripe__WebhookSecret=whsec_REPLACE_WITH_YOUR_STRIPE_WEBHOOK_SECRET

# BunnyCDN Video & Storage
Bunny__ApiKey=REPLACE_WITH_BUNNY_API_KEY
Bunny__PullZoneUrl=https://video.siriupskill.com
Bunny__TokenAuthKey=REPLACE_WITH_BUNNY_TOKEN_AUTH_KEY
Bunny__StreamLibraryId=REPLACE_WITH_STREAM_LIBRARY_ID

# Domain & CORS
Cors__AllowedOrigins__0=https://siriupskill.com
Cors__AllowedOrigins__1=https://admin.siriupskill.com
Seo__PublicBaseUrl=https://siriupskill.com
"@

$outputPath = Join-Path (Get-Location) $OutputEnvFile
[System.IO.File]::WriteAllText($outputPath, $envContent)

Write-Host ""
Write-Host "✓ Production secrets generated successfully!" -ForegroundColor Green
Write-Host "Target file: $outputPath" -ForegroundColor Yellow
Write-Host "DataProtection Key: $dataProtectionKey" -ForegroundColor DarkGray
Write-Host "JWT Signing Key:    $jwtSigningKey" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Action required: Open '$OutputEnvFile' and configure real Stripe & Bunny credentials." -ForegroundColor Cyan
