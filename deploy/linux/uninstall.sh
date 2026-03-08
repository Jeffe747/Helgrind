#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this uninstaller as root." >&2
  exit 1
fi

SOURCE_DIR="${HELGRIND_SOURCE_DIR:-/opt/helgrind-src}"
INSTALL_DIR="${HELGRIND_INSTALL_DIR:-/opt/helgrind}"
STATE_DIR="${HELGRIND_STATE_DIR:-/var/lib/helgrind}"
CONFIG_DIR="${HELGRIND_CONFIG_DIR:-/etc/helgrind}"
SERVICE_NAME="${HELGRIND_SERVICE_NAME:-helgrind}"
SERVICE_USER="${HELGRIND_SERVICE_USER:-helgrind}"
SERVICE_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
SUDOERS_PATH="/etc/sudoers.d/helgrind-update"

echo "Removing Helgrind installation..."
echo "Service: ${SERVICE_NAME}.service"
echo "Source dir: $SOURCE_DIR"
echo "Install dir: $INSTALL_DIR"
echo "State dir: $STATE_DIR"
echo "Config dir: $CONFIG_DIR"

if command -v systemctl >/dev/null 2>&1; then
  systemctl disable --now "${SERVICE_NAME}.service" >/dev/null 2>&1 || true
fi

rm -f "$SERVICE_PATH"
rm -f "$SUDOERS_PATH"
rm -rf "$INSTALL_DIR" "$SOURCE_DIR" "$STATE_DIR" "$CONFIG_DIR"

if command -v systemctl >/dev/null 2>&1; then
  systemctl daemon-reload || true
fi

if id -u "$SERVICE_USER" >/dev/null 2>&1; then
  userdel "$SERVICE_USER" >/dev/null 2>&1 || true
fi

echo "Helgrind has been removed."