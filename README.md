# Helgrind

Helgrind is a reusable .NET 10 reverse proxy gateway built on YARP. It combines a static management UI, SQLite-backed configuration, import and export support, and PEM certificate upload for Kestrel HTTPS hosting.

## Current MVP

- .NET 10 ASP.NET Core app named `Helgrind`
- YARP runtime configuration driven from SQLite instead of hardcoded JSON
- Static management UI in `wwwroot` using `index.html`, `app.js`, and `styles.css`
- Three-column layout: routes, clusters, and editable detail view
- Split HTTPS listeners: one public proxy listener and one private admin listener
- LAN-only protection for the admin listener based on configurable CIDR ranges
- Quick-setup fields for host routing, path matching, destinations, and active health checks
- Import and export for the quick-setup model
- PEM and key upload for the certificate used by Kestrel
- Visible restart warning when a stored certificate has not been picked up by Kestrel yet
- Restart helper scripts for Windows and Linux hosts
- Unit tests for configuration mapping, normalization/import logic, and certificate runtime behavior

## Project Layout

- `Helgrind/` main web application
- `Helgrind/Data/` SQLite entities and DbContext
- `Helgrind/Services/` runtime config, certificate, and configuration services
- `Helgrind/Endpoints/` management API endpoints for the UI
- `Helgrind/wwwroot/` static frontend
- `Helgrind.Tests/` logic unit tests

## How It Works

Helgrind stores a quick-setup configuration model in SQLite. That model is transformed into YARP route and cluster definitions by `ProxyConfigFactory`, then pushed into a custom in-memory `IProxyConfigProvider` so proxy changes can be applied without restarting the app.

Helgrind now runs two HTTPS listeners:

- a public listener for reverse proxy traffic
- an admin listener for the dashboard and management API

The admin listener is restricted to the configured LAN CIDR ranges. Requests from outside those networks are rejected before the UI or API is served.

The management UI talks to the backend using `/api/admin/*` endpoints:

- `GET /api/admin/configuration` returns the current editable model
- `PUT /api/admin/configuration` saves the draft to SQLite
- `POST /api/admin/apply` validates and applies the proxy config live
- `GET /api/admin/export` exports the quick-setup model as JSON
- `POST /api/admin/import` imports a previously exported JSON package
- `POST /api/admin/certificate` uploads and activates a PEM and key

## Certificate Behavior

Helgrind starts with a generated temporary self-signed certificate if no PEM and key have been uploaded yet. That allows the app to boot and serve the UI immediately.

When you upload a PEM and key through the UI:

- the files are stored under `Helgrind/App_Data/certificates/`
- certificate metadata is saved in SQLite
- the uploaded certificate is staged for Kestrel and becomes active after Helgrind is restarted

The dashboard shows when the currently served certificate does not match the stored certificate and exposes restart helper script names for the common host workflows.

Exports include certificate metadata only. The raw PEM and key are intentionally excluded, so restoring a full deployment still requires uploading the certificate material separately.

## Port 443 and Hosting Notes

Production defaults to these ports:

- public proxy: `443`
- admin dashboard: `8444`

Development defaults to these ports through `appsettings.Development.json`:

- public proxy: `8443`
- admin dashboard: `9443`

Using port `443` may require elevated permissions or environment-specific configuration:

- Windows may require running with sufficient privileges depending on the environment and binding rules.
- Linux commonly requires `CAP_NET_BIND_SERVICE`, a reverse proxy in front, or elevated privileges.

## Run Locally

```powershell
dotnet build Helgrind.slnx
dotnet run --project Helgrind/Helgrind.csproj
```

Open the configured HTTPS endpoint in your browser. In development, that is typically `https://localhost:8443`.

For the dashboard in development, use `https://localhost:9443`.

For the admin health page in development, use `https://localhost:9443/health.html`.

The dashboard also shows the active environment name and the effective HTTPS endpoint so you can tell at a glance whether you are looking at development or production settings.

The admin listener also exposes a small health page that shows:

- public and admin listener status
- served certificate state and restart requirement
- loaded route, cluster, and destination counts
- the currently loaded routes

## Public And Admin Split

The intended deployment shape is:

- expose only the public proxy port externally
- keep the admin dashboard port off router forwards and public load balancers
- allow the admin listener only from your LAN, VPN, or other explicitly allowed private ranges

The allowed admin networks are configured in `Helgrind:AllowedAdminNetworks` in appsettings.

Default allowed admin ranges are:

- `127.0.0.0/8`
- `::1/128`
- `10.0.0.0/8`
- `172.16.0.0/12`
- `192.168.0.0/16`
- `fc00::/7`
- `fe80::/10`

If you use Tailscale, WireGuard, or another overlay network for admin access, add the relevant CIDR ranges here.

Because the fallback certificate is self-signed, your browser will warn until you upload a trusted certificate.

## Run Tests

```powershell
dotnet test Helgrind.Tests/Helgrind.Tests.csproj
```

## Restart Helpers

Windows PowerShell:

```powershell
./scripts/restart-helgrind.ps1
```

Windows PowerShell without rebuilding:

```powershell
./scripts/restart-helgrind.ps1 -NoBuild
```

Linux or WSL:

```bash
./scripts/restart-helgrind.sh
```

Linux or WSL without rebuilding:

```bash
./scripts/restart-helgrind.sh ./Helgrind/Helgrind.csproj no-build
```

## Linux systemd Deployment

Helgrind includes `systemd` deployment files in [deploy/systemd/helgrind.service](/e:/Development/Helgrind/deploy/systemd/helgrind.service) and [deploy/systemd/helgrind.env.example](/e:/Development/Helgrind/deploy/systemd/helgrind.env.example).

The intended Linux shape is:

- publish the app to `/opt/helgrind`
- store writable state under `/var/lib/helgrind`
- expose only the public proxy port externally
- keep the admin port private to LAN or VPN networks

Example publish command:

```bash
dotnet publish ./Helgrind/Helgrind.csproj -c Release -o ./publish/linux
```

Example installation flow:

```bash
sudo useradd --system --home /opt/helgrind --shell /usr/sbin/nologin helgrind
sudo mkdir -p /opt/helgrind /etc/helgrind /var/lib/helgrind
sudo cp -R ./publish/linux/* /opt/helgrind/
sudo cp ./deploy/systemd/helgrind.service /etc/systemd/system/helgrind.service
sudo cp ./deploy/systemd/helgrind.env.example /etc/helgrind/helgrind.env
sudo chown -R root:root /opt/helgrind
sudo chown -R helgrind:helgrind /var/lib/helgrind /etc/helgrind
sudo chmod 640 /etc/helgrind/helgrind.env
sudo systemctl daemon-reload
sudo systemctl enable --now helgrind.service
```

Useful service commands:

```bash
sudo systemctl status helgrind
sudo systemctl restart helgrind
sudo journalctl -u helgrind -f
```

The shipped unit preserves the split listener model:

- public proxy listener on `443`
- private admin listener on `8444`
- `CAP_NET_BIND_SERVICE` so Helgrind can bind `443` without running as root
- writable state isolated to `/var/lib/helgrind`

If you need different ports or extra private networks for the admin listener, change `/etc/helgrind/helgrind.env` and restart the service.

Recommended firewall posture on Linux:

- allow `443/tcp` from anywhere if Helgrind is your public edge
- allow `8444/tcp` only from your LAN, VPN, or overlay network ranges
- do not publish or forward the admin port on your router

## Quick Setup Flow

1. Start Helgrind.
2. Open the UI.
3. Add one or more clusters and destinations.
4. Add routes that reference those clusters.
5. Save the draft.
6. Apply the proxy configuration.
7. Upload the PEM and key used for the public HTTPS endpoint.
8. Export the configuration once the setup is stable.

## What This MVP Does Not Cover Yet

- Advanced YARP transforms and header manipulation
- Authentication or authorization for the admin UI
- ACME or Let's Encrypt automation
- Full parity with every YARP configuration setting
- Browser end-to-end tests

## Notes For Your Example Topology

The current quick-setup model is intentionally generic. It does not pre-seed your specific domains or destination placeholders, but it supports the same shape:

- one route per host
- one cluster per backend target group
- one or more destinations per cluster
- optional active health checks with interval, timeout, policy, path, query, and threshold

That keeps Helgrind usable outside your current homelab setup while still covering the structure you described.
