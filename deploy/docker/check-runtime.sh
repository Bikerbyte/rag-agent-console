#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1}"
RUNTIME_URL="${BASE_URL%/}/api/runtime"

echo "Checking runtime endpoint: ${RUNTIME_URL}"

runtime_json="$(curl -fsS "${RUNTIME_URL}")"

python3 -c '
import json, sys
payload = json.loads(sys.stdin.read())
runtime = payload.get("runtime", {})
print("")
print("Runtime Summary")
print(f"  InstanceName   : {payload.get(\"instanceName\")}")
print(f"  Environment    : {payload.get(\"environment\")}")
print(f"  ProcessId      : {payload.get(\"processId\")}")
print(f"  StartedAt      : {payload.get(\"startedAt\")}")
print(f"  Profile        : {runtime.get(\"profile\")}")
print(f"  LeadershipLease: {runtime.get(\"enableLeadershipLease\")}")
print(f"  LeaseDuration  : {runtime.get(\"leaseDurationSeconds\")}s")
print(f"  LeaseRenew     : {runtime.get(\"leaseRenewIntervalSeconds\")}s")
print(f"  LeaseRetry     : {runtime.get(\"leaseAcquireRetrySeconds\")}s")
print(f"  WebhookIngress : {runtime.get(\"enableTelegramWebhookIngress\")}")
print(f"  PollingWorker  : {runtime.get(\"enableTelegramPollingWorker\")}")
print(f"  QueueWorker    : {runtime.get(\"enableTelegramUpdateQueueWorker\")}")
print(f"  OfficialSync   : {runtime.get(\"enableOfficialDataSyncWorker\")}")
print(f"  Notification   : {runtime.get(\"enableNotificationWorker\")}")
' <<< "${runtime_json}"

echo ""
echo "Response headers from /"
curl -sSI "${BASE_URL%/}/" | grep -Ei "HTTP/|X-App-Instance|Server:" || true
