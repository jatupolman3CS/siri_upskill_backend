#!/usr/bin/env bash
set -euo pipefail

OUTPUT_ENV_FILE="${1:-.env.production}"

echo "=========================================================="
echo "     SiriUpSkill Production Secret Generator (P7-12)      "
echo "=========================================================="

AES_KEY=$(openssl rand -base64 32)
JWT_KEY=$(openssl rand -base64 48)
DB_PASS="Siri_$(openssl rand -hex 12)!"
REDIS_PASS=$(openssl rand -hex 20)

cat <<EOF > "$OUTPUT_ENV_FILE"
# ==============================================================================
# SiriUpSkill Production Environment Configuration
# Generated on: $(date -u +"%Y-%m-%d %H:%M:%S UTC")
# CAUTION: NEVER COMMIT THIS FILE TO VERSION CONTROL
# ==============================================================================

# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000

# Database (PostgreSQL 17 — task P0-41)
POSTGRES_PASSWORD=$DB_PASS
ConnectionStrings__Default=Host=postgres;Port=5432;Database=SIRIUPSKILL;Username=siriupskill_app;Password=$DB_PASS;Maximum Pool Size=200

# Redis
REDIS_PASSWORD=$REDIS_PASS
ConnectionStrings__Redis=redis:6379,password=$REDIS_PASS,ssl=false,abortConnect=false,connectTimeout=5000

# Data Protection (AES-256 32-byte key)
DataProtection__EncryptionKeyBase64=$AES_KEY

# Identity & JWT
Identity__Jwt__SigningKey=$JWT_KEY
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
EOF

chmod 600 "$OUTPUT_ENV_FILE"

echo ""
echo "✓ Production secrets generated successfully into $OUTPUT_ENV_FILE"
echo "Action required: Configure Stripe and Bunny production credentials."
