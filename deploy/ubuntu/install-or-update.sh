#!/usr/bin/env bash
set -euo pipefail

APP_NAME="cpbl-telegram-assistant"
INSTALL_ROOT="/opt/${APP_NAME}"
APP_DIR="${INSTALL_ROOT}/app"
ENV_DIR="/etc/${APP_NAME}"
ENV_FILE="${ENV_DIR}/${APP_NAME}.env"
SERVICE_PATH="/etc/systemd/system/${APP_NAME}.service"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Please run this script with sudo."
  exit 1
fi

echo "[1/6] Preparing directories..."
install -d -m 755 "${INSTALL_ROOT}" "${APP_DIR}" "${ENV_DIR}"
chown -R www-data:www-data "${INSTALL_ROOT}"

echo "[2/6] Publishing application..."
dotnet publish "${REPO_ROOT}/CPBLLineBotCloud.csproj" -c Release -o "${APP_DIR}"
chown -R www-data:www-data "${APP_DIR}"

echo "[3/6] Installing systemd service..."
install -m 644 "${SCRIPT_DIR}/cpbl-telegram-assistant.service" "${SERVICE_PATH}"

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "[4/6] Creating initial env file from example..."
  install -m 640 "${SCRIPT_DIR}/cpbl-telegram-assistant.env.example" "${ENV_FILE}"
else
  echo "[4/6] Keeping existing env file at ${ENV_FILE}"
fi

echo "[5/6] Reloading systemd..."
systemctl daemon-reload
systemctl enable "${APP_NAME}.service"
systemctl restart "${APP_NAME}.service"

echo "[6/6] Service status:"
systemctl --no-pager --full status "${APP_NAME}.service" || true

cat <<EOF

Deployment finished.

Next steps:
1. Edit ${ENV_FILE}
2. Run: systemctl restart ${APP_NAME}
3. Run: journalctl -u ${APP_NAME} -n 100 --no-pager
EOF
