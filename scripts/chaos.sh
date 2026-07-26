#!/usr/bin/env bash
# Milestone 4 — chaos suite. Break each moving part while orders are in flight and assert the
# guarantees hold: nothing is lost (every order reaches a terminal state) and the effect is
# exactly-once (one payment per order, no duplicates) — even across a broker, consumer, or
# database failure. Run `make up` first. Prints a results table; exit 1 if any invariant breaks.
set -uo pipefail

API="${API:-http://localhost:8080}"
RMQ="${RMQ:-http://guest:guest@localhost:15672}"
DC="docker compose"
ROWS=()

say()  { printf '\n\033[1m== %s\033[0m\n' "$1"; }
place() { curl -sf -X POST "$API/orders" -H 'Content-Type: application/json' \
            -d "{\"customer\":\"chaos\",\"amount\":$1}" 2>/dev/null | grep -o '"id":"[^"]*' | cut -d'"' -f4; }
place_batch() { local n=$1 amt=$2 out=""; for _ in $(seq 1 "$n"); do out="$out $(place "$amt")"; done; echo "$out"; }
status() { curl -sf "$API/orders/$1" 2>/dev/null | grep -o '"status":"[^"]*' | cut -d'"' -f4; }
terminal() { [ "$1" = "Shipped" ] || [ "$1" = "PaymentFailed" ]; }
count_terminal() { local c=0; for id in $1; do terminal "$(status "$id")" && c=$((c+1)); done; echo "$c"; }
wait_terminal() { local ids="$1" timeout=$2 t; for t in $(seq 1 "$timeout"); do
    local pend=0; for id in $ids; do terminal "$(status "$id")" || pend=$((pend+1)); done
    [ "$pend" -eq 0 ] && return 0; sleep 1; done; return 1; }
wait_api() { local t; for t in $(seq 1 90); do curl -sf "$API/health" >/dev/null 2>&1 && return 0; sleep 1; done; return 1; }
wait_pg()  { local t; for t in $(seq 1 90); do $DC exec -T postgres pg_isready -U postgres >/dev/null 2>&1 && return 0; sleep 1; done; return 1; }
psql1() { $DC exec -T postgres psql -U postgres -d "$1" -tAc "$2" 2>/dev/null | tr -d '[:space:]'; }
dlq()  { curl -sf "$RMQ/api/queues/%2f/payments.ordering.events.dlq" 2>/dev/null | grep -o '"messages":[0-9]*' | head -1 | cut -d: -f2; }

# scenario <name> <ids> <placed>
record() {
  local name="$1" ids="$2" placed="$3"
  local term; term=$(count_terminal "$ids"); local lost=$((placed - term))
  local verdict="PASS"; [ "$lost" -ne 0 ] && verdict="FAIL"
  ROWS+=("$(printf '%-34s %8s %10s %6s   %s' "$name" "$placed" "$term" "$lost" "$verdict")")
  printf '   %s: %s/%s terminal, %s lost -> %s\n' "$name" "$term" "$placed" "$lost" "$verdict"
}

say "Baseline — no chaos"
ids=$(place_batch 10 250); wait_terminal "$ids" 60; record "baseline" "$ids" 10

say "Kill the BROKER mid-flight, and place more while it is down (outbox durability)"
ids=$(place_batch 8 250)
$DC kill rabbitmq >/dev/null 2>&1; echo "   broker killed"; sleep 3
extra=$(place_batch 6 250); echo "   placed 6 orders while the broker was down (they sit in the outbox)"
$DC start rabbitmq >/dev/null 2>&1; echo "   broker restarted"
wait_terminal "$ids $extra" 120; record "kill broker (+place while down)" "$ids $extra" 14

say "Kill a CONSUMER (payments) mid-flight — durable queue holds the work"
ids=$(place_batch 10 250)
$DC kill payments >/dev/null 2>&1; echo "   payments consumer killed"; sleep 4
$DC start payments >/dev/null 2>&1; echo "   payments restarted"
wait_terminal "$ids" 120; record "kill consumer" "$ids" 10

say "Kill the DATABASE mid-flight — in-flight transactions fail transiently and retry"
ids=$(place_batch 10 250); sleep 1
$DC kill postgres >/dev/null 2>&1; echo "   postgres killed"; sleep 4
$DC start postgres >/dev/null 2>&1; wait_pg && wait_api; echo "   postgres restarted"
wait_terminal "$ids" 150; record "kill database" "$ids" 10

say "Duplicate delivery — replay the same message 3x, assert one payment (exactly-once effect)"
did=$(place 250); wait_terminal "$did" 40
for _ in 1 2 3; do curl -sf -X POST "$API/orders/$did/replay" >/dev/null 2>&1; done; sleep 3
dupN=$(psql1 payments "select count(*) from payments where \"OrderId\"='$did'")
[ "$dupN" = "1" ] && dv="PASS" || dv="FAIL"
ROWS+=("$(printf '%-34s %8s %10s %6s   %s' "duplicate delivery (replay x3)" "1" "$dupN payment" "" "$dv")")
printf '   payments for that order after 3 replays: %s -> %s\n' "$dupN" "$dv"

say "Poison message — a malformed order is dead-lettered, order untouched"
before=$(dlq); pid=$(place 0)
after=$before; for _ in $(seq 1 25); do after=$(dlq); [ "$after" -gt "$before" ] && break; sleep 1; done
pstatus=$(status "$pid")
{ [ "$((before+1))" = "$after" ] && [ "$pstatus" = "Placed" ]; } && pv="PASS" || pv="FAIL"
ROWS+=("$(printf '%-34s %8s %10s %6s   %s' "poison -> DLQ (order stays Placed)" "1" "DLQ ${before}->${after}" "" "$pv")")
printf '   DLQ %s -> %s, order status %s -> %s\n' "$before" "$after" "$pstatus" "$pv"

say "Global invariant — exactly-once effect across everything above"
pt=$(psql1 payments "select count(*) from payments")
pd=$(psql1 payments "select count(distinct \"OrderId\") from payments")
st=$(psql1 shipping "select count(*) from shipments")
sd=$(psql1 shipping "select count(distinct \"OrderId\") from shipments")
{ [ "$pt" = "$pd" ] && [ "$st" = "$sd" ]; } && gv="PASS" || gv="FAIL"
printf '   payments rows=%s distinct-orders=%s ; shipments rows=%s distinct-orders=%s -> %s\n' "$pt" "$pd" "$st" "$sd" "$gv"

say "Results"
printf '%-34s %8s %10s %6s   %s\n' "scenario" "placed" "terminal" "lost" "verdict"
printf '%s\n' "----------------------------------------------------------------------------"
for r in "${ROWS[@]}"; do printf '%s\n' "$r"; done
printf '%-34s %8s %10s %6s   %s\n' "exactly-once (no duplicate effects)" "" "" "" "$gv"

echo
if printf '%s\n' "${ROWS[@]}" "$gv" | grep -q FAIL; then
  echo "CHAOS SUITE: FAIL"; exit 1
else
  echo "CHAOS SUITE: PASS — no loss under failure, exactly-once effect held."; exit 0
fi
