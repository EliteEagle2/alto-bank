#!/usr/bin/env bash
# Runs the end-to-end test suites against a TEST server and saves each log to ../test-logs/.
#
#   B=http://localhost:5299 bash tests/run_all.sh            # every suite, in order
#   B=http://localhost:5299 bash tests/run_all.sh part3 p2p  # just these
#
# Start the test server on a SCRATCH database first (never the real altobank.db), e.g. in PowerShell:
#   $env:ConnectionStrings__DefaultConnection = "Data Source=C:\temp\alto-test.db"
#   dotnet run -- --reset-db
#   $env:ASPNETCORE_URLS = "http://localhost:5299"; dotnet run --no-launch-profile
# Suites that "move time" (p2p, scheduler, ...) also need TEST_DB=<that scratch .db file> (see lib.sh sql()),
# and the server started with $env:Scheduler__IntervalSeconds = "5" so background jobs run quickly.
# The suites build on each other's data, so run them in this order on a freshly reset database.
B=${B:?set B to the TEST server url (never the real app on 5264)}
export B
HERE="$(cd "$(dirname "$0")" && pwd)"
LOGS="$HERE/../test-logs"; mkdir -p "$LOGS"
ALL="part1 apply race part2 review2 part3 review3 p2p billpay staff profile"
SUITES="${*:-$ALL}"
TOTAL_PASS=0; TOTAL_FAIL=0
for s in $SUITES; do
  if [ "$s" = review3 ]; then
    # Phase 2 is meant to run after a server restart (to prove the startup backfill); without one it still checks the rules.
    { bash "$HERE/test_review3.sh" phase1; bash "$HERE/test_review3.sh" phase2; } > "$LOGS/review3.log" 2>&1
  else
    bash "$HERE/test_$s.sh" > "$LOGS/$s.log" 2>&1
  fi
  p=$(grep -o 'RESULT: [0-9]* passed' "$LOGS/$s.log" | awk '{s+=$2} END {print s+0}')
  f=$(grep -o 'passed, [0-9]* failed' "$LOGS/$s.log" | awk '{s+=$2} END {print s+0}')
  TOTAL_PASS=$((TOTAL_PASS + ${p:-0})); TOTAL_FAIL=$((TOTAL_FAIL + ${f:-0}))
  printf '%-10s %4s passed, %s failed\n' "$s" "${p:-0}" "${f:-0}"
  grep 'FAIL' "$LOGS/$s.log"
done
echo "TOTAL: $TOTAL_PASS passed, $TOTAL_FAIL failed"
[ "$TOTAL_FAIL" = 0 ]
