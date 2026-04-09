#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1}"
LEASE_URL="${BASE_URL%/}/api/runtime/leases"

echo "Checking leadership leases: ${LEASE_URL}"

lease_json="$(curl -fsS "${LEASE_URL}")"

python3 -c '
import json, sys
payload = json.loads(sys.stdin.read())
leases = payload.get("leases", [])
print("")
print(f"ServerTime: {payload.get(\"serverTime\")}")
print(f"LeaseCount: {payload.get(\"count\")}")
if not leases:
    print("No leadership leases found yet.")
    sys.exit(0)
print("")
print("Leadership Leases")
for lease in leases:
    print(f"- {lease.get(\"leaseName\")}")
    print(f"  Owner          : {lease.get(\"ownerInstanceName\")}")
    print(f"  Active         : {lease.get(\"isActive\")}")
    print(f"  AcquiredAt     : {lease.get(\"acquiredAt\")}")
    print(f"  RenewedAt      : {lease.get(\"renewedAt\")}")
    print(f"  ExpiresAt      : {lease.get(\"expiresAt\")}")
    print(f"  ExpiresInSec   : {lease.get(\"expiresInSeconds\")}")
' <<< "${lease_json}"
