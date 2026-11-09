#!/usr/bin/env bash
# PART 3 review fixes. Run "phase1", restart the test server, then run "phase2".
source "$(dirname "$0")/lib.sh"; wait_up
A=$(mktemp); demo $A admin >/dev/null
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
MCHK=$(curl -s -b $M $B/api/accounts | grep -oE '"id":[0-9]+,"type":"Checking"' | head -1 | grep -oE '[0-9]+')
flash() { curl -s -b $1 -c $1 "$2" | tr -s ' \r\n' ' ' | grep -oE 'alert-alto-(success|error)[^>]*> *<i[^>]*></i>[^<]+' | sed 's/.*<\/i>//' | head -1; }
active_cards() { curl -s -b $A $B/admin/accounts/details/$MCHK | tr -s ' \r\n' ' ' | sed 's/.*kpi-label mb-2">Cards//; s/admin-card p-0.*//' | grep -oE '(Active|Frozen) *</span>' | wc -l | tr -d ' '; }
echo "Maya checking=$MCHK"
case "$1" in
phase1)
  echo "== Cancel Maya's card (no longer needed)"
  CARD=$(curl -s -b $M $B/accounts/$MCHK | grep -oE "/accounts/$MCHK/cards/[0-9]+/freeze" | head -1 | grep -oE 'cards/[0-9]+' | grep -oE '[0-9]+')
  R=$(post $M /accounts/$MCHK /accounts/$MCHK/cards/$CARD/cancel --data-urlencode "reason=No longer needed")
  has "card cancelled" "$(flash $M $B/accounts/$MCHK)" "is cancelled"
  echo "$CARD" > /tmp/review3_card
  echo "== CSV formula guard (tab prefix)"
  R=$(post $M /transactions/deposit /transactions/deposit --data-urlencode "AccountId=$MCHK" -d Amount=1.00 --data-urlencode $'Description=\t=HYPERLINK("x")')
  CSV=$(curl -s -b $M "$B/accounts/$MCHK/activity.csv")
  has "tab-prefixed description is neutralized with a leading quote" "$CSV" $'\'\t=HYPERLINK'
  hasnt "no raw tab-formula cell" "$CSV" $',\t=HYPERLINK'
  has "admin export neutralized too" "$(curl -s -b $A "$B/admin/transactions/export")" $'\'\t=HYPERLINK'
  ;;
phase2)
  CARD=$(cat /tmp/review3_card)
  echo "== After restart: the backfill did NOT issue a second card"
  check "Maya still has 0 usable cards after a restart" "$(active_cards)" 0
  echo "== Customer requests a new card: exactly one usable card"
  R=$(post $M /accounts/$MCHK /accounts/$MCHK/cards/$CARD/replace -d reason=Lost)
  has "replacement issued" "$(flash $M $B/accounts/$MCHK)" "Your new card ends in"
  check "exactly one usable card" "$(active_cards)" 1
  ;;
esac
summary
