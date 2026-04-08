#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:5050}"
ENV_FILE="${2:-/etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env}"
HEALTH_URL="${BASE_URL%/}/api/telegram/health"

read_env_value() {
  local key="$1"
  local file="$2"
  if [[ ! -f "${file}" ]]; then
    return 1
  fi

  local line
  line="$(grep -E "^${key}=" "${file}" | tail -n 1 || true)"
  if [[ -z "${line}" ]]; then
    return 1
  fi

  line="${line#*=}"
  line="${line%\"}"
  line="${line#\"}"
  printf "%s" "${line}"
}

echo "Checking local Telegram health: ${HEALTH_URL}"
health_json="$(curl -fsS "${HEALTH_URL}")"

python3 -c '
import json, sys
payload = json.loads(sys.stdin.read())
print("")
print("Telegram Health")
for key in ("Provider", "Enabled", "HasBotToken", "UseWebhookMode", "WebhookPath", "EnableTelegramUpdateQueueWorker"):
    print(f"  {key:28}: {payload.get(key)}")
' <<< "${health_json}"

bot_token="$(read_env_value "TelegramBot__BotToken" "${ENV_FILE}" || true)"
expected_webhook_url="$(read_env_value "TelegramBot__WebhookUrl" "${ENV_FILE}" || true)"

if [[ -z "${bot_token}" ]]; then
  echo ""
  echo "Skip Telegram getWebhookInfo because bot token is not configured in ${ENV_FILE}."
  exit 0
fi

echo ""
echo "Checking Telegram getWebhookInfo from Bot API..."

webhook_json="$(curl -fsS "https://api.telegram.org/bot${bot_token}/getWebhookInfo")"

python3 -c '
import json, os, sys
payload = json.loads(sys.stdin.read())
result = payload.get("result", {})
expected = os.environ.get("EXPECTED_WEBHOOK_URL", "")
print("")
print("Telegram Webhook Info")
print(f"  Url                : {result.get(\"url\")}")
print(f"  WebhookConfigured  : {bool(result.get(\"url\"))}")
print(f"  PendingUpdateCount : {result.get(\"pending_update_count\")}")
print(f"  LastErrorDate      : {result.get(\"last_error_date\")}")
print(f"  LastErrorMessage   : {result.get(\"last_error_message\")}")
print(f"  MaxConnections     : {result.get(\"max_connections\")}")
if expected:
    print(f"  ExpectedUrl        : {expected}")
    print(f"  UrlMatches         : {result.get(\"url\") == expected}")
' <<< "${webhook_json}"
