#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "This script must run as root. Configure the Helgrind update command to call it through sudo." >&2
  exit 1
fi

REPO_REF="${HELGRIND_REPO_REF:-main}"
SOURCE_DIR="${HELGRIND_SOURCE_DIR:-/opt/helgrind-src}"
INSTALL_DIR="${HELGRIND_INSTALL_DIR:-/opt/helgrind}"
SERVICE_NAME="${HELGRIND_SERVICE_NAME:-helgrind}"
PUBLISH_DIR="${HELGRIND_PUBLISH_DIR:-/tmp/helgrind-publish}"

if [[ ! -d "$SOURCE_DIR/.git" ]]; then
  echo "Helgrind source checkout not found at $SOURCE_DIR" >&2
  exit 1
fi

git -C "$SOURCE_DIR" fetch --tags origin
git -C "$SOURCE_DIR" checkout "$REPO_REF"
git -C "$SOURCE_DIR" pull --ff-only origin "$REPO_REF"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$SOURCE_DIR/Helgrind/Helgrind.csproj" -c Release -o "$PUBLISH_DIR"

mkdir -p "$INSTALL_DIR"
rsync -a --delete "$PUBLISH_DIR/" "$INSTALL_DIR/"
chown -R root:root "$INSTALL_DIR"

systemctl daemon-reload
systemctl restart "${SERVICE_NAME}.service"

rm -rf "$PUBLISH_DIR"

echo "Helgrind updated and restarted."