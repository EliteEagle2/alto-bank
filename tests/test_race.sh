#!/usr/bin/env bash
# Concurrency + service-level amount rules (Part 1 review fixes).
source "$(dirname "$0")/lib.sh"; wait_up
C=$(mktemp); demo $C customer >/dev/null

ACC=$(curl -s -b $C $B/api/accounts)
CHK=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Checking"' | head -1 | grep -oE '[0-9]+')
SAV=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Savings"' | head -1 | grep -oE '[0-9]+')
BAL=$(curl -s -b $C $B/api/accounts/$CHK | grep -oE '"balance":[0-9.]+' | cut -d: -f2)
AMT=$(awk -v b="$BAL" 'BEGIN { printf "%.2f", b * 0.6 }')
echo "checking #$CHK balance=$BAL; firing 8 simultaneous withdrawals of $AMT (only ONE can fit)"

T=$(tok $C /transactions/withdraw)
for i in $(seq 1 8); do
  curl -s -b $C -o /dev/null -w '%{http_code}\n' -d "AccountId=$CHK&Amount=$AMT&Description=race$i&__RequestVerificationToken=$T" $B/transactions/withdraw > /tmp/race_$i.txt &
done
wait
OKS=$(cat /tmp/race_*.txt | grep -c 302)
check "exactly one concurrent withdrawal succeeded" "$OKS" 1
NEW=$(curl -s -b $C $B/api/accounts/$CHK | grep -oE '"balance":[0-9.-]+' | cut -d: -f2)
EXP=$(awk -v b="$BAL" -v a="$AMT" 'BEGIN { printf "%.2f", b - a }')
check "balance = original - one withdrawal" "$(awk -v n="$NEW" 'BEGIN { printf "%.2f", n }')" "$EXP"
check "race rows recorded" "$(curl -s -b $C "$B/api/accounts/$CHK/transactions?page=1" | grep -o '"description":"race' | wc -l | tr -d ' ')" 1

echo "== Same race through the JSON API (transfers)"
BAL2=$(curl -s -b $C $B/api/accounts/$SAV | grep -oE '"balance":[0-9.]+' | cut -d: -f2)
AMT2=$(awk -v b="$BAL2" 'BEGIN { printf "%.2f", b * 0.6 }')
for i in $(seq 1 8); do
  curl -s -b $C -o /dev/null -w '%{http_code}\n' -H 'Content-Type: application/json' -d "{\"fromAccountId\":$SAV,\"toAccountId\":$CHK,\"amount\":$AMT2}" $B/api/transfers > /tmp/race2_$i.txt &
done
wait
check "exactly one concurrent API transfer succeeded" "$(cat /tmp/race2_*.txt | grep -c 200)" 1
NEW2=$(curl -s -b $C $B/api/accounts/$SAV | grep -oE '"balance":[0-9.-]+' | cut -d: -f2)
check "savings never negative" "$(awk -v n="$NEW2" 'BEGIN { print (n >= 0) ? "yes" : "no" }')" yes

echo "== Amount rules enforced in BankService (API path skips view models)"
J='Content-Type: application/json'
has "API transfer 3 decimals rejected" "$(curl -s -b $C -H "$J" -d "{\"fromAccountId\":$CHK,\"toAccountId\":$SAV,\"amount\":1.239}" $B/api/transfers)" "2 decimal places"
check "ownership: other user's tx page -> 404" "$(code -b $C "$B/api/accounts/999/transactions?page=1")" 404
summary
