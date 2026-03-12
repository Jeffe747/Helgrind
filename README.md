# Helgrind

.NET 10 reverse proxy gateway built on YARP. Admin UI for managing routes and clusters, SQLite-backed config, split public and admin HTTPS listeners, and suspicious-traffic telemetry.

## 📦 Installation

```bash
curl -fsSL https://raw.githubusercontent.com/Jeffe747/Helgrind/main/deploy/linux/install.sh | sudo bash 
```

Over SSH:

```bash
ssh ubuntu@your-server "curl -fsSL https://raw.githubusercontent.com/Jeffe747/Helgrind/main/deploy/linux/install.sh | sudo bash"
```

After the first install or any update: use the `Update` button in the dashboard header.

**Uninstall**:
```bash
sudo /bin/bash /opt/helgrind-src/deploy/linux/uninstall.sh
```

## 🔑 Security

- Admin dashboard is restricted to `Helgrind:AllowedAdminNetworks` — keep the admin port LAN-only or behind a VPN.
- No built-in admin authentication beyond network restriction.

## 🚀 Usage

- **Dashboard**: `https://<host>:8444/`
- **Health**: `https://<host>:8444/health.html`
- **Public proxy**: `https://<host>:443/`

### Basic Setup

1. Add clusters and destinations.
2. Add routes pointing to those clusters.
3. Save and apply the proxy configuration.
4. Upload a PEM and key for the HTTPS certificate.
5. Restart if the dashboard says the certificate is staged but not active yet.

### Database Configuration

Helgrind uses SQLite by default, saving your proxy configuration and telemetry to `App_Data/helgrind.db` (`/var/lib/helgrind/helgrind.db` on Linux installations). 

You can optionally configure Helgrind to use PostgreSQL instead. In the Linux deployment, edit `/etc/helgrind/helgrind.env` and append the following overrides:

```bash
Helgrind__DatabaseProvider=Postgres
Helgrind__PostgresConnectionString=Host=localhost;Database=helgrind;Username=postgres;Password=mypassword
```

Then restart the service: `sudo systemctl restart helgrind`.

### Quick Start (Dev)

```powershell
dotnet run --project Helgrind/Helgrind.csproj
```

Dev ports: admin `9443`, public `8443`.

## 🛠 Troubleshooting

- **Logs**: `journalctl -u helgrind -f`
- **Service file**: `/etc/systemd/system/helgrind.service`
- **Re-run update**: `sudo /opt/helgrind-src/deploy/linux/update.sh`
- **Deployed ref**: `cat /var/lib/helgrind/deployed-ref.txt`

## 📄 Status

Helgrind runs as a split-listener YARP proxy with a LAN-only admin dashboard. Includes certificate upload and management, suspicious-traffic telemetry with optional webhook alerting, and admin-triggered self-update.
