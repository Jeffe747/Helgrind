#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this installer as root, for example: curl ... | sudo bash" >&2
  exit 1
fi

REPO_URL="${HELGRIND_REPO_URL:-https://github.com/Jeffe747/Helgrind.git}"
REPO_REF="${HELGRIND_REPO_REF:-main}"
SOURCE_DIR="${HELGRIND_SOURCE_DIR:-/opt/helgrind-src}"
INSTALL_DIR="${HELGRIND_INSTALL_DIR:-/opt/helgrind}"
STATE_DIR="${HELGRIND_STATE_DIR:-/var/lib/helgrind}"
CONFIG_DIR="${HELGRIND_CONFIG_DIR:-/etc/helgrind}"
SERVICE_NAME="${HELGRIND_SERVICE_NAME:-helgrind}"
PUBLIC_PORT="${HELGRIND_PUBLIC_PORT:-443}"
ADMIN_PORT="${HELGRIND_ADMIN_PORT:-8444}"

export DEBIAN_FRONTEND=noninteractive

apt-get update
apt-get install -y curl git rsync ca-certificates gnupg apt-transport-https wget python3 sudo

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  source /etc/os-release
  wget -q https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
  dpkg -i /tmp/packages-microsoft-prod.deb
  rm -f /tmp/packages-microsoft-prod.deb
  apt-get update
  apt-get install -y dotnet-sdk-10.0
fi

id -u helgrind >/dev/null 2>&1 || useradd --system --home "$INSTALL_DIR" --shell /usr/sbin/nologin helgrind

mkdir -p "$SOURCE_DIR" "$INSTALL_DIR" "$STATE_DIR" "$CONFIG_DIR"

if [[ ! -d "$SOURCE_DIR/.git" ]]; then
  rm -rf "$SOURCE_DIR"
  git clone --branch "$REPO_REF" --single-branch "$REPO_URL" "$SOURCE_DIR"
else
  git -C "$SOURCE_DIR" fetch --tags origin
  git -C "$SOURCE_DIR" checkout "$REPO_REF"
  git -C "$SOURCE_DIR" pull --ff-only origin "$REPO_REF"
fi

install -d -m 0755 /etc/systemd/system
cp "$SOURCE_DIR/deploy/systemd/helgrind.service" "/etc/systemd/system/${SERVICE_NAME}.service"

if [[ ! -f "$CONFIG_DIR/helgrind.env" ]]; then
  cp "$SOURCE_DIR/deploy/systemd/helgrind.env.example" "$CONFIG_DIR/helgrind.env"
fi

python3 - "$CONFIG_DIR/helgrind.env" "$STATE_DIR" "$PUBLIC_PORT" "$ADMIN_PORT" "$SOURCE_DIR" <<'PY'
from pathlib import Path
import sys

env_path = Path(sys.argv[1])
state_dir = sys.argv[2]
public_port = sys.argv[3]
admin_port = sys.argv[4]
source_dir = sys.argv[5]

lines = env_path.read_text(encoding="utf-8").splitlines()
updates = {
    "Helgrind__PublicHttpsPort": public_port,
    "Helgrind__AdminHttpsPort": admin_port,
    "Helgrind__DatabasePath": f"{state_dir}/helgrind.db",
    "Helgrind__CertificateStoragePath": f"{state_dir}/certificates",
    "Helgrind__SelfUpdateWorkingDirectory": source_dir,
}

for key, value in updates.items():
    prefix = f"{key}="
    for index, line in enumerate(lines):
        if line.startswith(prefix):
            lines[index] = f"{key}={value}"
            break
    else:
        lines.append(f"{key}={value}")

env_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
PY

cat > /etc/sudoers.d/helgrind-update <<SUDOERS
helgrind ALL=(root) NOPASSWD: /bin/bash ${SOURCE_DIR}/deploy/linux/update.sh
SUDOERS
chmod 440 /etc/sudoers.d/helgrind-update

chown -R root:root "$SOURCE_DIR" "$INSTALL_DIR"
chown -R helgrind:helgrind "$STATE_DIR" "$CONFIG_DIR"
chmod 640 "$CONFIG_DIR/helgrind.env"

bash "$SOURCE_DIR/deploy/linux/update.sh"

systemctl daemon-reload
systemctl enable --now "${SERVICE_NAME}.service"

echo "Helgrind installed."
echo "Public listener: https://<server>:${PUBLIC_PORT}"
echo "Admin listener: https://<server>:${ADMIN_PORT}"