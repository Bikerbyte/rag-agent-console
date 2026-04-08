#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:5050}"
LEASE_URL="${BASE_URL%/}/api/runtime/leases"

echo "Checking leadership leases: ${LEASE_URL}"

lease_json="$(curl -fsS "${LEASE_URL}")"

python3 -c '
import json, sys
payload = json.loads(sys.stdin.read())
leases = payload.get("Leases", [])
print("")
print(f"ServerTime: {payload.get(\"ServerTime\")}")
print(f"LeaseCount: {payload.get(\"Count\")}")
if not leases:
    print("No leadership leases found yet.")
    sys.exit(0)
print("")
print("Leadership Leases")
for lease in leases:
    print(f"- {lease.get(\"LeaseName\")}")
    print(f"  Owner          : {lease.get(\"OwnerInstanceName\")}")
    print(f"  Active         : {lease.get(\"IsActive\")}")
    print(f"  AcquiredAt     : {lease.get(\"AcquiredAt\")}")
    print(f"  RenewedAt      : {lease.get(\"RenewedAt\")}")
    print(f"  ExpiresAt      : {lease.get(\"ExpiresAt\")}")
    print(f"  ExpiresInSec   : {lease.get(\"ExpiresInSeconds\")}")
' <<< "${lease_json}"
