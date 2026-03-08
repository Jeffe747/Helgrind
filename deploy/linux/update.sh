#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "This script must run as root. Configure the Helgrind update command to call it through sudo." >&2
  exit 1
fi

REPO_URL="${HELGRIND_REPO_URL:-https://github.com/Jeffe747/Helgrind.git}"
REPO_REF="${HELGRIND_REPO_REF:-main}"
SOURCE_DIR="${HELGRIND_SOURCE_DIR:-/opt/helgrind-src}"
INSTALL_DIR="${HELGRIND_INSTALL_DIR:-/opt/helgrind}"
STATE_DIR="${HELGRIND_STATE_DIR:-/var/lib/helgrind}"
CONFIG_DIR="${HELGRIND_CONFIG_DIR:-/etc/helgrind}"
SERVICE_NAME="${HELGRIND_SERVICE_NAME:-helgrind}"
PUBLISH_DIR="${HELGRIND_PUBLISH_DIR:-/tmp/helgrind-publish}"
UPDATE_LOG_PATH="${HELGRIND_UPDATE_LOG:-${STATE_DIR}/update.log}"
DEPLOYED_REF_PATH="${HELGRIND_DEPLOYED_REF_FILE:-${STATE_DIR}/deployed-ref.txt}"
SERVICE_USER="${HELGRIND_SERVICE_USER:-helgrind}"
SERVICE_GROUP="${HELGRIND_SERVICE_GROUP:-helgrind}"
PUBLIC_PORT="${HELGRIND_PUBLIC_PORT:-443}"
ADMIN_PORT="${HELGRIND_ADMIN_PORT:-8444}"
SERVICE_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
SERVICE_SOURCE_PATH="${SOURCE_DIR}/deploy/systemd/helgrind.service"
ENV_SOURCE_PATH="${SOURCE_DIR}/deploy/systemd/helgrind.env.example"
SUDOERS_PATH="/etc/sudoers.d/helgrind-update"

mkdir -p "$SOURCE_DIR" "$INSTALL_DIR" "$STATE_DIR" "$CONFIG_DIR"
mkdir -p "$(dirname "$UPDATE_LOG_PATH")" "$(dirname "$DEPLOYED_REF_PATH")"
touch "$UPDATE_LOG_PATH"
exec > >(tee -a "$UPDATE_LOG_PATH") 2>&1

echo "[$(date -u +'%Y-%m-%dT%H:%M:%SZ')] Starting Helgrind update."
echo "Repository: $REPO_URL ($REPO_REF)"
echo "Source dir: $SOURCE_DIR"
echo "Install dir: $INSTALL_DIR"

if [[ ! -d "$SOURCE_DIR/.git" ]]; then
  rm -rf "$SOURCE_DIR"
  git clone --branch "$REPO_REF" --single-branch "$REPO_URL" "$SOURCE_DIR"
fi

if ! id -u "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --home "$INSTALL_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

git -C "$SOURCE_DIR" remote set-url origin "$REPO_URL"
git -C "$SOURCE_DIR" fetch --prune --tags origin

if git -C "$SOURCE_DIR" show-ref --verify --quiet "refs/remotes/origin/$REPO_REF"; then
  git -C "$SOURCE_DIR" checkout -B "$REPO_REF" "origin/$REPO_REF"
  git -C "$SOURCE_DIR" reset --hard "origin/$REPO_REF"
else
  git -C "$SOURCE_DIR" checkout "$REPO_REF"
  git -C "$SOURCE_DIR" reset --hard "$REPO_REF"
fi

git -C "$SOURCE_DIR" clean -fdx

DEPLOYED_COMMIT="$(git -C "$SOURCE_DIR" rev-parse HEAD)"
echo "Deploying commit: $DEPLOYED_COMMIT"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$SOURCE_DIR/Helgrind/Helgrind.csproj" -c Release -o "$PUBLISH_DIR"

mkdir -p "$INSTALL_DIR"
rsync -a --delete "$PUBLISH_DIR/" "$INSTALL_DIR/"

if [[ ! -f "$SERVICE_SOURCE_PATH" ]]; then
  echo "Expected systemd unit template was not found at: $SERVICE_SOURCE_PATH" >&2
  exit 1
fi

install -d -m 0755 /etc/systemd/system
install -m 0644 "$SERVICE_SOURCE_PATH" "$SERVICE_PATH"

if [[ ! -f "$CONFIG_DIR/helgrind.env" ]]; then
  if [[ ! -f "$ENV_SOURCE_PATH" ]]; then
    echo "Expected environment template was not found at: $ENV_SOURCE_PATH" >&2
    exit 1
  fi

  install -m 0640 "$ENV_SOURCE_PATH" "$CONFIG_DIR/helgrind.env"
fi

python3 - "$CONFIG_DIR/helgrind.env" "$STATE_DIR" "$PUBLIC_PORT" "$ADMIN_PORT" "$SOURCE_DIR" "$INSTALL_DIR" "$REPO_URL" "$REPO_REF" <<'PY'
from pathlib import Path
import sys

env_path = Path(sys.argv[1])
state_dir = sys.argv[2]
public_port = sys.argv[3]
admin_port = sys.argv[4]
source_dir = sys.argv[5]
install_dir = sys.argv[6]
repo_url = sys.argv[7]
repo_ref = sys.argv[8]

lines = env_path.read_text(encoding="utf-8").splitlines()
updates = {
  "Helgrind__PublicHttpsPort": public_port,
  "Helgrind__AdminHttpsPort": admin_port,
  "Helgrind__DatabasePath": f"{state_dir}/helgrind.db",
  "Helgrind__CertificateStoragePath": f"{state_dir}/certificates",
  "HELGRIND_SOURCE_DIR": source_dir,
  "HELGRIND_INSTALL_DIR": install_dir,
  "HELGRIND_STATE_DIR": state_dir,
  "HELGRIND_REPO_URL": repo_url,
  "HELGRIND_REPO_REF": repo_ref,
  "HELGRIND_UPDATE_LOG": f"{state_dir}/update.log",
  "HELGRIND_DEPLOYED_REF_FILE": f"{state_dir}/deployed-ref.txt",
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

cat > "$SUDOERS_PATH" <<SUDOERS
${SERVICE_USER} ALL=(root) NOPASSWD: /bin/bash ${INSTALL_DIR}/deploy/linux/update.sh
SUDOERS
chmod 440 "$SUDOERS_PATH"

chown -R root:root "$INSTALL_DIR"
chown -R root:root "$SOURCE_DIR"
chown -R "$SERVICE_USER:$SERVICE_GROUP" "$STATE_DIR" "$CONFIG_DIR"
chmod 640 "$CONFIG_DIR/helgrind.env"
touch "$UPDATE_LOG_PATH" "$DEPLOYED_REF_PATH"
chown "$SERVICE_USER:$SERVICE_GROUP" "$UPDATE_LOG_PATH" "$DEPLOYED_REF_PATH"

printf '%s\n' "$DEPLOYED_COMMIT" > "$DEPLOYED_REF_PATH"

echo "Systemd unit installed at: $SERVICE_PATH"
echo "Recorded deployed commit in: $DEPLOYED_REF_PATH"

systemctl daemon-reload

if systemctl list-unit-files "${SERVICE_NAME}.service" --no-legend 2>/dev/null | grep -q "^${SERVICE_NAME}\.service"; then
  systemctl enable "${SERVICE_NAME}.service"
else
  echo "Systemd did not list ${SERVICE_NAME}.service after daemon-reload; enabling via explicit path." >&2
  systemctl enable "$SERVICE_PATH"
  systemctl daemon-reload
fi

if systemctl is-active --quiet "${SERVICE_NAME}.service"; then
  systemctl restart "${SERVICE_NAME}.service"
else
  systemctl start "${SERVICE_NAME}.service"
fi

rm -rf "$PUBLISH_DIR"

echo "Update log written to: $UPDATE_LOG_PATH"
echo "Deployed commit recorded in: $DEPLOYED_REF_PATH"
echo "Systemd unit ensured at: $SERVICE_PATH"

echo "Helgrind updated and restarted."