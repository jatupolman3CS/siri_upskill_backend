# Uptime & Log Monitoring Setup

Two shared, VPS-wide monitoring instances exist on the Contabo box (they watch other
projects on the same host too — see `docs/PROGRESS.md`'s "VPS deploy reality" note):

| URL | Service | Purpose |
|---|---|---|
| `https://kuma.siristudiophoto.com/` | Uptime Kuma | HTTP uptime checks |
| `https://monitor.siristudiophoto.com/` | .NET Aspire Dashboard | OTLP structured logs / traces / metrics viewer |

Confirmed 2026-09-21 by loading both pages directly: Kuma shows its login screen; the
`monitor.` host serves the Aspire Dashboard's "Structured logs" page (no resources connected
yet). Probing `/v1/logs` and `/v1/traces` on the public host both return 404 identical to a
nonexistent path — the OTLP ingest port is **not** reverse-proxied publicly, only the
dashboard UI is. Confirming the real ingest address needs VPS shell access (see step 2).

## 1. Uptime Kuma — add HTTP monitors

Requires logging into the Kuma UI directly (Claude Code will not enter a password into any
login form — see `.claude/rules/security.md`). Log in and add:

| Monitor | URL | Expected |
|---|---|---|
| API health | `https://siriupskill.siristudiophoto.com/health` | 200 |
| Homepage | `https://siriupskill.siristudiophoto.com` | 200 |
| Bunny playback sample | (a real signed/sample playback URL from `VideoProvider`) | 200 |

Matches the plan already in `docs/DEPLOYMENT.md`'s "Monitoring & alert" section.

## 2. Wire backend logs/traces/metrics into the Aspire Dashboard

The application side needs **no code change** — `ObservabilityOptions`/`Program.cs` (P0-13)
already export logs (Serilog OTel sink), traces and metrics to any OTLP collector the moment
`Observability:OtlpEndpoint` is set; blank means disabled (current default, safe no-op).

What's missing is the *value* of that endpoint, which only VPS shell access can reveal. Run
on the VPS (or via `ssh root@217.217.253.122 "...same command..."` from a machine with the
key):

```bash
docker ps --format '{{.Names}}\t{{.Image}}\t{{.Ports}}' | grep -i aspire
docker inspect <container-name-from-above> \
  --format '{{json .NetworkSettings.Networks}}{{println}}{{range .Config.Env}}{{println .}}{{end}}' \
  | grep -iE 'otlp|network|port|key'
```

That output gives three things needed to finish this:

1. **Docker network name** the Aspire Dashboard container is on — `siri_upskill_backend`'s
   container needs `--network <that-name>` added to its `docker run`/Jenkins job (same
   pattern as `siri-net` for Postgres, see `docs/PROGRESS.md`'s P0-41 note) or it can't reach
   the dashboard by container name at all.
2. **OTLP port** (Aspire's default is gRPC `18889`, separate from the dashboard UI's own
   port) — becomes `Observability__OtlpEndpoint=http://<container-name>:<port>` in
   `.env_prd`.
3. Whether `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS` or an `OtlpApiKey`/
   `DOTNET_DASHBOARD_OTLP_API_KEY` is set — if there's an API key, it needs to be sent as the
   `x-otlp-api-key` header, which isn't currently plumbed through `ObservabilityOptions` (it
   only carries the endpoint + protocol) and would need a small follow-up change once known.

Once the endpoint (and API key, if any) are known, set `Observability__OtlpEndpoint` (and add
API-key support if needed) — no further backend code changes otherwise.
