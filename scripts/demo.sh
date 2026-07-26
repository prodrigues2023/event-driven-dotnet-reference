#!/usr/bin/env bash
# The reliability spine, demonstrated end to end against the running stack (make up first):
#  1. happy path      — an order flows Placed -> Paid -> Shipped across three services
#  2. business decline — an over-limit order ends Placed -> PaymentFailed (a valid event, not a failure)
#  3. poison message   — a malformed order is dead-lettered, not retried forever; the order is untouched
#  4. replay           — re-dispatching the same MessageId is deduplicated by the inbox (exactly-once effect)
set -euo pipefail

API="${API:-http://localhost:8080}"
RMQ="${RMQ:-http://guest:guest@localhost:15672}"

say() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
place() { curl -sf -X POST "$API/orders" -H 'Content-Type: application/json' -d "$1"; }
field() { grep -o "\"$1\":\"\?[^\",}]*" | head -1 | sed 's/.*://; s/"//g'; }
status() { curl -sf "$API/orders/$1" | field status; }
dlq_count() { curl -sf "$RMQ/api/queues/%2f/payments.ordering.events.dlq" 2>/dev/null | grep -o '"messages":[0-9]*' | head -1 | cut -d: -f2; }

poll() { # id target
  for _ in $(seq 1 40); do s=$(status "$1"); printf '   status: %s\n' "$s"; [ "$s" = "$2" ] && return 0; sleep 1; done
  echo "   did not reach $2 in time"; return 1
}

say "1. Happy path — order for 250.00"
OID=$(place '{"customer":"Ana Silva","amount":250.00}' | field id); echo "   order $OID"
poll "$OID" "Shipped"
curl -sf "$API/orders/$OID"; echo

say "2. Business decline — order for 9000.00 (over the authorization limit)"
OID2=$(place '{"customer":"Big Corp","amount":9000.00}' | field id); echo "   order $OID2"
poll "$OID2" "PaymentFailed"

say "3. Poison message — order for 0 (malformed): dead-lettered, order stays Placed"
BEFORE=$(dlq_count || echo 0)
OID3=$(place '{"customer":"Broken Order","amount":0}' | field id); echo "   order $OID3"
sleep 5
echo "   order $OID3 status: $(status "$OID3")  (expected: Placed — payment never happened)"
echo "   payments DLQ depth: $(dlq_count) (was $BEFORE)"

say "4. Replay the happy order — same MessageId re-dispatched, deduplicated by the inbox"
curl -sf -X POST "$API/orders/$OID/replay"; echo
sleep 3
echo "   watch the payments log for 'Duplicate ... already processed — skipping'"
echo "   run: docker compose logs payments | grep Duplicate"

say "Done. No message lost (outbox), exactly-once effect (inbox), failures observable (DLQ)."
