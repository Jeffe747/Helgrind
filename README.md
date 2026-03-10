# Helgrind

Helgrind is a .NET 10 reverse proxy gateway built on YARP. It gives you a small admin UI for managing routes and clusters, stores configuration in SQLite, and serves traffic through a separate public HTTPS listener.

## Linux Install And Update

Systemd files are included in [deploy/systemd/helgrind.service](/e:/Development/Helgrind/deploy/systemd/helgrind.service) and [deploy/systemd/helgrind.env.example](/e:/Development/Helgrind/deploy/systemd/helgrind.env.example).

The intended Ubuntu flow is: 

- install with `deploy/linux/install.sh`
- keep the source checkout in `/opt/helgrind-src`
- publish the runnable app into `/opt/helgrind`
- keep SQLite and certificates in `/var/lib/helgrind`
- let the admin update button call `deploy/linux/update.sh`

Install script:

- installs Git, rsync, and .NET 10 SDK
- clones the repo
- sets up the `helgrind` user, systemd service, and env file
- adds a narrow sudo rule so the service user can run the update script only
- configures the production self-update command
- publishes and starts the service

Update script:

- pulls the latest code
- hard-resets the dedicated source checkout to the configured remote branch tip
- removes stray files from the dedicated source checkout before publishing
- republishes the app
- replaces files in `/opt/helgrind`
- ensures `/etc/systemd/system/helgrind.service` and `/etc/helgrind/helgrind.env` exist
- enables and starts `helgrind.service`
- writes `/var/lib/helgrind/update.log` and `/var/lib/helgrind/deployed-ref.txt`

Remove the installation:
 
```bash
sudo /bin/bash /opt/helgrind-src/deploy/linux/uninstall.sh
``` 

Example install command:

```bash
curl -fsSL https://raw.githubusercontent.com/Jeffe747/Helgrind/main/deploy/linux/install.sh | sudo bash
```

Over SSH:

```bash
ssh ubuntu@your-server "curl -fsSL https://raw.githubusercontent.com/Jeffe747/Helgrind/main/deploy/linux/install.sh | sudo bash"
```

Optional overrides:

- `HELGRIND_REPO_REF` if you want a branch or tag other than `main`
- `HELGRIND_REPO_URL` if you fork the repo later

## What It Does

- Static admin UI in `wwwroot`
- SQLite-backed route and cluster configuration
- Live proxy apply without restarting the app
- Import and export of the editable config model
- PEM and key upload for the app's HTTPS certificate
- Split listeners: public proxy and private admin dashboard
- LAN-only access controls for the admin listener
- Admin-triggered self-update from the LAN-only dashboard
- Admin health page at `/health.html`

## Default Ports

- Development: public `8443`, admin `9443`
- Production: public `443`, admin `8444`

Only the public listener should be exposed externally. Keep the admin port private to your LAN, VPN, or other trusted network.

## Quick Start

```powershell
dotnet build Helgrind.slnx
dotnet run --project Helgrind/Helgrind.csproj
```

Then open:

- Dashboard: `https://localhost:9443/`
- Health: `https://localhost:9443/health.html`
- Public proxy listener: `https://localhost:8443/`

## Basic Setup Flow

1. Add clusters and destinations.
2. Add routes that point to those clusters.
3. Save the draft.
4. Apply the proxy configuration.
5. Upload a PEM and key for the app certificate.
6. Restart Helgrind if the dashboard says the certificate is staged but not active yet.

## Certificate Behavior

If no certificate has been uploaded yet, Helgrind starts with a temporary self-signed certificate so the UI is still reachable.

When you upload a PEM and key:

- the files are stored on disk
- certificate metadata is saved in SQLite
- the certificate becomes active on the next app restart

Exports include certificate metadata only, not the raw PEM or key.

## Configuration Notes

- Admin network restrictions are controlled by `Helgrind:AllowedAdminNetworks`.
- Proxy config is stored in SQLite and translated into YARP runtime config in memory.
- The dashboard and API are only served from the admin listener.
- The update button is hidden in Development.
- In Production, the update button works automatically when the standard Ubuntu source checkout exists at `/opt/helgrind-src` and contains `deploy/linux/update.sh`.
- `Helgrind:SelfUpdateCommand` is still available as an override if you want a custom update flow.
- The update button deploys the configured remote branch, typically `origin/main`. It does not deploy unpushed local workspace changes.

## Telemetry

Helgrind now records suspicious public-listener traffic in SQLite for 30 days by default.

It currently flags:

- common exploit paths such as `/.env`, `/.git`, `/wp-admin`, and `/phpmyadmin`
- unsupported methods such as `TRACE`, `CONNECT`, and `TRACK`
- unmatched public route misses and host mismatches
- short-window burst activity from one source IP

It does not store request bodies, cookies, or authorization headers.

Smoke-test path:

- public path: `Helgrind:TelemetrySmokePath`
- default value: `/__helgrind/telemetry/smoke`
- behavior: Helgrind returns a local `404` from the public listener and records a `SmokeTest` telemetry event without forwarding the request to any backend

Example validation flow:

```powershell
Invoke-WebRequest -Uri https://localhost:8443/__helgrind/telemetry/smoke -SkipCertificateCheck
Invoke-RestMethod -Uri https://localhost:9443/api/admin/telemetry/events?hours=1&page=1&pageSize=10 -SkipCertificateCheck
```

Optional alerting:

- set `Helgrind:TelemetryAlertWebhookUrl` to a Discord webhook URL or another endpoint that accepts `{"content":"..."}` JSON
- `Helgrind:TelemetryAlertMinimumRiskScore` defaults to `3` for high-risk events only
- `Helgrind:TelemetryAlertCooldownMinutes` defaults to `10` to avoid alert floods

## Restart Helpers

Windows:

```powershell
./scripts/restart-helgrind.ps1
./scripts/restart-helgrind.ps1 -NoBuild
```

Linux or WSL:

```bash
./scripts/restart-helgrind.sh
./scripts/restart-helgrind.sh ./Helgrind/Helgrind.csproj no-build
```

## Tests

```powershell
dotnet test Helgrind.Tests/Helgrind.Tests.csproj
```

## Project Layout

- `Helgrind/` main web app
- `Helgrind/Data/` SQLite entities and DbContext
- `Helgrind/Services/` config, certificate, and runtime services
- `Helgrind/Endpoints/` admin API endpoints
- `Helgrind/wwwroot/` static frontend
- `Helgrind.Tests/` unit tests

## Current Limits

- No built-in admin authentication beyond network restriction
- No ACME or Let's Encrypt automation
- Not a full surface-area editor for every YARP feature
