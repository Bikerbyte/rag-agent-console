#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:5050}"
RUNTIME_URL="${BASE_URL%/}/api/runtime"

echo "Checking runtime endpoint: ${RUNTIME_URL}"

runtime_json="$(curl -fsS "${RUNTIME_URL}")"

python3 -c '
import json, sys
payload = json.loads(sys.stdin.read())
runtime = payload.get("Runtime", {})
print("")
print("Runtime Summary")
print(f"  InstanceName   : {payload.get(\"InstanceName\")}")
print(f"  Environment    : {payload.get(\"Environment\")}")
print(f"  ProcessId      : {payload.get(\"ProcessId\")}")
print(f"  StartedAt      : {payload.get(\"StartedAt\")}")
print(f"  LeadershipLease: {runtime.get(\"EnableLeadershipLease\")}")
print(f"  LeaseDuration  : {runtime.get(\"LeaseDurationSeconds\")}s")
print(f"  LeaseRenew     : {runtime.get(\"LeaseRenewIntervalSeconds\")}s")
print(f"  LeaseRetry     : {runtime.get(\"LeaseAcquireRetrySeconds\")}s")
print(f"  WebhookIngress : {runtime.get(\"EnableTelegramWebhookIngress\")}")
print(f"  PollingWorker  : {runtime.get(\"EnableTelegramPollingWorker\")}")
print(f"  QueueWorker    : {runtime.get(\"EnableTelegramUpdateQueueWorker\")}")
print(f"  OfficialSync   : {runtime.get(\"EnableOfficialDataSyncWorker\")}")
print(f"  Notification   : {runtime.get(\"EnableNotificationWorker\")}")
' <<< "${runtime_json}"

echo ""
echo "Response headers from /"
curl -sSI "${BASE_URL%/}/" | grep -Ei "HTTP/|X-App-Instance|Server:" || true
