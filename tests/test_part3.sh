#!/usr/bin/env bash
# PART 3 — customer Account Details page (/accounts/{id}), activity API, cards, statements, direct deposit.
source "$(dirname "$0")/lib.sh"; wait_up
J='Content-Type: application/json'
C=$(mktemp); demo $C customer >/dev/null                 # Jordan
A=$(mktemp); demo $A admin >/dev/null
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
flash() { curl -s -b $1 -c $1 "$2" | tr -s ' \r\n' ' ' | grep -oE 'alert-alto-(success|error)[^>]*> *<i[^>]*></i>[^<]+' | sed 's/.*<\/i>//' | head -1; }
ACC=$(curl -s -b $C $B/api/accounts)
CHK=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Checking"' | head -1 | grep -oE '[0-9]+')
SAV=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Savings"' | head -1 | grep -oE '[0-9]+')
VAC=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Savings"' | sed -n 2p | grep -oE '[0-9]+')
MAYA=$(curl -s -b $M $B/api/accounts | grep -oE '"id":[0-9]+' | head -1 | grep -oE '[0-9]+')
echo "Jordan checking=$CHK savings=$SAV vacation=$VAC; Maya account=$MAYA"

echo "== Page + IDOR"
D=$(curl -s -b $C $B/accounts/$CHK)
check "own account details 200" "$(code -b $C $B/accounts/$CHK)" 200
check "someone else's account -> 404 (IDOR)" "$(code -b $C $B/accounts/$MAYA)" 404
check "non-existent account -> 404" "$(code -b $C $B/accounts/999999)" 404
check "anonymous -> login" "$(code $B/accounts/$CHK)" 302
check "admin can't use customer page (role)" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $A $B/accounts/$CHK | grep -ci accessdenied)" 1
has "header: _AccountHeader with masked number" "$D" '\*\*\*\*  4821'
has "header: current + available balance" "$D" 'Available balance'
has "tabs: Activity / Account Info / Direct Deposit / Card" "$(echo "$D" | grep -oE 'data-bs-target="#(activity|info|direct-deposit|card)"' | sort -u | wc -l | tr -d ' ')" 4
hasnt "savings has no Card tab" "$(curl -s -b $C $B/accounts/$SAV)" 'data-bs-target="#card"'
has "savings header: APY + interest this year" "$(curl -s -b $C $B/accounts/$SAV)" 'Interest this year'
has "savings header: goal progress" "$(curl -s -b $C $B/accounts/$SAV)" 'Emergency Fund'
ROWS=$(echo "$D" | grep -c 'class="tx-row')
check "first page: 20 rows via _TransactionRow" "$ROWS" 20
has "rows grouped by date with sticky headers" "$D" 'class="tx-date-head"'
has "row details (expand) include transaction id + counterparty" "$D" 'Counterparty</dt>'
has "money in / out / net summary" "$D" 'data-sum="net"'
has "filters partial (presets)" "$D" 'data-preset="year"'
has "load more button shown" "$D" 'data-load-more >'
has "dashboard cards link to /accounts/{id}" "$(curl -s -b $C $B/dashboard)" "href=\"/accounts/$CHK\""

echo "== Activity API (filters + paging)"
P1=$(curl -s -b $C "$B/api/accounts/$CHK/transactions?pageSize=20&page=1")
P2=$(curl -s -b $C "$B/api/accounts/$CHK/transactions?pageSize=20&page=2")
check "page 1 has 20 items" "$(echo "$P1" | grep -o '"id":' | wc -l | tr -d ' ')" 20
has "page 1 hasMore" "$P1" '"hasMore":true'
FIRST2=$(echo "$P2" | grep -oE '"id":[0-9]+' | head -1); hasnt "page 2 continues (no overlap)" "$P1" "$FIRST2,"
check "kind=in -> only credits" "$(curl -s -b $C "$B/api/accounts/$CHK/transactions?pageSize=50&kind=in" | grep -o '"isCredit":false' | wc -l | tr -d ' ')" 0
check "kind=out -> only debits" "$(curl -s -b $C "$B/api/accounts/$CHK/transactions?pageSize=50&kind=out" | grep -o '"isCredit":true' | wc -l | tr -d ' ')" 0
SP=$(curl -s -b $C "$B/api/accounts/$CHK/transactions?pageSize=50&search=payroll")
check "search=payroll -> only payroll rows" "$(echo "$SP" | grep -oE '"description":"[^"]*"' | grep -vic payroll)" 0
FROM=$(date -u -d '-30 days' +%Y-%m-%d 2>/dev/null || date -u -v-30d +%Y-%m-%d)
check "date filter (from = 30 days ago) -> nothing older" "$(curl -s -b $C "$B/api/accounts/$CHK/transactions?pageSize=50&from=$FROM" | grep -oE '"date":"[0-9-]{10}' | cut -d'"' -f4 | awk -v f="$FROM" '$1 < f' | wc -l | tr -d ' ')" 0
NET=$(echo "$P1" | grep -oE '"moneyIn":[0-9.]+,"moneyOut":[0-9.]+,"net":-?[0-9.]+' | awk -F'[:,]' '{ printf "%.2f %.2f", $2-$4, $6 }')
check "net = money in - money out" "$(echo $NET | cut -d' ' -f1)" "$(echo $NET | cut -d' ' -f2)"
check "pageSize 51 -> 400" "$(code -b $C "$B/api/accounts/$CHK/transactions?pageSize=51")" 400
check "other user's activity -> 404" "$(code -b $C "$B/api/accounts/$MAYA/transactions")" 404

echo "== CSV download"
TOTAL=$(curl -s -b $C "$B/api/accounts/$CHK/transactions?pageSize=1&kind=out" | grep -oE '"totalCount":[0-9]+' | cut -d: -f2)
H=$(curl -s -D - -o /tmp/act.csv -b $C "$B/accounts/$CHK/activity.csv?kind=out")
has "CSV content type" "$H" "text/csv"
has "CSV header" "$(head -1 /tmp/act.csv)" "Date (UTC),Description,Type,Status,Amount"
check "CSV rows = filtered total" "$(($(wc -l < /tmp/act.csv) - 1))" "$TOTAL"
check "other user's CSV -> 404" "$(code -b $C "$B/accounts/$MAYA/activity.csv")" 404

echo "== Account number (Show) + routing"
N=$(curl -s -b $C $B/api/accounts/$CHK/number)
has "full account number returned to owner" "$N" '"accountNumber":"4410274821"'
has "routing number 031300000" "$N" '"routingNumber":"031300000"'
check "other user's number -> 404" "$(code -b $C $B/api/accounts/$MAYA/number)" 404
hasnt "full number NOT in the page HTML" "$D" '4410274821'
has "reveal is audited" "$(curl -s -b $A "$B/admin/auditlog?action=Account.RevealNumber")" "Viewed full account number"
has "direct deposit tab: routing + bank address + printable form" "$(echo "$D" | grep -c 'id="ddForm"')$(echo "$D" | grep -c '1200 Chestnut Ave')" "1"

echo "== Nickname (inline edit)"
has "rename" "$(curl -s -b $C -H "$J" -d '{"nickname":"Daily Spending"}' $B/api/accounts/$CHK/nickname)" '"name":"Daily Spending"'
has "  ...shows on the page" "$(curl -s -b $C $B/accounts/$CHK)" 'data-account-name>Daily Spending'
check "31 characters -> 400" "$(code -b $C -H "$J" -d '{"nickname":"ABCDEFGHIJABCDEFGHIJABCDEFGHIJX"}' $B/api/accounts/$CHK/nickname)" 400
check "rename someone else's account -> 404" "$(code -b $C -H "$J" -d '{"nickname":"Mine now"}' $B/api/accounts/$MAYA/nickname)" 404
curl -s -o /dev/null -b $C -H "$J" -d '{"nickname":"Everyday Checking"}' $B/api/accounts/$CHK/nickname

echo "== Statements"
Y=$(date -u +%Y); MO=$(date -u +%-m); PY=$(date -u -d "$(date -u +%Y-%m-01) -1 month" +%Y); PM=$(date -u -d "$(date -u +%Y-%m-01) -1 month" +%-m)
ST=$(curl -s -b $C "$B/accounts/$CHK/statements/$PY/$PM")
check "last month's statement 200" "$(code -b $C "$B/accounts/$CHK/statements/$PY/$PM")" 200
has "statement: summary + rows" "$ST" "Closing balance"
OPEN=$(echo "$ST" | grep -oE 'Opening balance</span><strong>\$[0-9,.]+' | grep -oE '[0-9,.]+$' | tr -d ,)
IN=$(echo "$ST" | grep -oE 'class="in">\+\$[0-9,.]+' | grep -oE '[0-9,.]+$' | tr -d ,)
OUT=$(echo "$ST" | grep -oE 'class="out">−\$[0-9,.]+' | head -1 | grep -oE '[0-9,.]+$' | tr -d ,)
CLOSE=$(echo "$ST" | grep -oE 'Closing balance</span><strong>\$[0-9,.]+' | grep -oE '[0-9,.]+$' | tr -d ,)
check "statement math: opening + in - out = closing" "$(awk -v o=$OPEN -v i=$IN -v x=$OUT 'BEGIN{printf "%.2f", o+i-x}')" "$(awk -v c=$CLOSE 'BEGIN{printf "%.2f", c}')"
check "month-to-date statement 200" "$(code -b $C "$B/accounts/$CHK/statements/$Y/$MO")" 200
check "other user's statement -> 404" "$(code -b $C "$B/accounts/$MAYA/statements/$PY/$PM")" 404
check "month 13 -> 404" "$(code -b $C "$B/accounts/$CHK/statements/$Y/13")" 404

echo "== Redirects into the Activity tab"
check "/accounts/{id}/history -> details#activity" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $C $B/accounts/$CHK/history)" "$B/accounts/$CHK#activity"
R=$(post $C /transactions/deposit /transactions/deposit -d "AccountId=$CHK&Amount=12.34&Description=Part3 deposit")
check "deposit -> details#activity" "$(echo "$R" | cut -d' ' -f2)" "$B/accounts/$CHK#activity"

echo "== Card tab"
CD=$(curl -s -b $C $B/accounts/$CHK)
CARD=$(echo "$CD" | grep -oE "/accounts/$CHK/cards/[0-9]+/freeze" | grep -oE 'cards/[0-9]+' | cut -d/ -f2)
L4=$(echo "$CD" | grep -oE 'Alto Debit ••[0-9]{4}' | grep -oE '[0-9]{4}')
echo "  card #$CARD ••$L4"
RV=$(curl -s -b $C "$B/api/accounts/$CHK/cards/$CARD/reveal")
has "reveal: 16-digit number" "$RV" '"cardNumber":"4410 [0-9]\{4\} [0-9]\{4\} [0-9]\{4\}"'
has "reveal: 3-digit CVV" "$RV" '"cvv":"[0-9]\{3\}"'
check "CVV is stable (derived, not random)" "$(echo "$RV" | grep -oE '"cvv":"[0-9]+"')" "$(curl -s -b $C "$B/api/accounts/$CHK/cards/$CARD/reveal" | grep -oE '"cvv":"[0-9]+"')"
hasnt "full card number not in page HTML" "$CD" "$(echo "$RV" | grep -oE '"cardNumber":"[^"]+"' | cut -d'"' -f4)"
MCARD=$(curl -s -b $M "$B/accounts/$MAYA" | grep -oE "/cards/[0-9]+/freeze" | grep -oE '[0-9]+' | head -1)
check "someone else's card -> 404" "$(code -b $C "$B/api/accounts/$CHK/cards/$MCARD/reveal")" 404
check "card id with the wrong account -> 404" "$(code -b $C "$B/api/accounts/$SAV/cards/$CARD/reveal")" 404
check "freeze someone else's card -> 404" "$(post $C /accounts/$CHK /accounts/$MAYA/cards/$MCARD/freeze | cut -d' ' -f1)" 404
R=$(post $C /accounts/$CHK /accounts/$CHK/cards/$CARD/freeze)
check "freeze -> back to #card" "$(echo "$R" | cut -d' ' -f2)" "$B/accounts/$CHK#card"
has "  ...frozen" "$(flash $C $B/accounts/$CHK)" "is frozen"
has "  ...card art shows frozen" "$(curl -s -b $C $B/accounts/$CHK)" 'is-frozen'
R=$(post $C /accounts/$CHK /accounts/$CHK/cards/$CARD/unfreeze); has "unfreeze" "$(flash $C $B/accounts/$CHK)" "active again"
R=$(post $C /accounts/$CHK /accounts/$CHK/cards/$CARD/cancel -d reason=Bogus); has "cancel needs a valid reason" "$(flash $C $B/accounts/$CHK)" "Choose a reason"
R=$(post $C /accounts/$CHK /accounts/$CHK/cards/$CARD/cancel --data-urlencode "reason=No longer needed"); has "cancel" "$(flash $C $B/accounts/$CHK)" "is cancelled"
check "cancelled card can't be revealed (400)" "$(code -b $C "$B/api/accounts/$CHK/cards/$CARD/reveal")" 400
has "request-new-card offered" "$(curl -s -b $C $B/accounts/$CHK)" 'replaceCardModal'
R=$(post $C /accounts/$CHK /accounts/$CHK/cards/$CARD/replace -d reason=Lost); has "replace issues a new card" "$(flash $C $B/accounts/$CHK)" "Your new card ends in"
CD2=$(curl -s -b $C $B/accounts/$CHK); NL4=$(echo "$CD2" | grep -oE 'Alto Debit ••[0-9]{4}' | grep -oE '[0-9]{4}')
[ -n "$NL4" ] && [ "$NL4" != "$L4" ] && ok "new card has a new number (••$NL4)" || bad "new card number missing or same"
has "card history lists the old card" "$CD2" "••$L4"
R=$(post $C /accounts/$CHK /accounts/$CHK/cards/$CARD/replace -d reason=Lost); has "can't replace the same card twice" "$(flash $C $B/accounts/$CHK)" "already issued"
for a in Card.Freeze Card.Unfreeze Card.Cancel Card.Replace Card.Reveal; do
  n=$(curl -s -b $A "$B/admin/auditlog?action=$a" | grep -o "<code>$a</code>" | wc -l | tr -d ' '); [ "$n" -ge 1 ] && ok "audit: $a" || bad "audit missing $a"
done
hasnt "audit log never contains a full card number" "$(curl -s -b $A "$B/admin/auditlog/export")" "$(echo "$RV" | grep -oE '"cardNumber":"[^"]+"' | cut -d'"' -f4)"

echo "== Frozen + closed banners, actions disabled"
T=$(tok $A /admin/accounts/details/$CHK); curl -s -o /dev/null -b $A -c $A --data-urlencode 'reason=Banner test' --data-urlencode "__RequestVerificationToken=$T" $B/admin/accounts/freeze/$CHK
FD=$(curl -s -b $C $B/accounts/$CHK)
has "frozen banner" "$FD" "This account is frozen"
has "withdraw disabled" "$FD" 'disabled" aria-disabled="true" title="Paused while this account is frozen"><i class="bi bi-arrow-up-circle'
has "deposit still enabled" "$FD" "/transactions/deposit?accountId=$CHK"
has "available balance \$0.00 while frozen" "$(echo "$FD" | tr -s ' \r\n' ' ')" 'Available balance</div> <div class="acct-available"><span class="money">$0.00'
T=$(tok $A /admin/accounts/details/$CHK); curl -s -o /dev/null -b $A -c $A --data-urlencode 'reason=Banner test done' --data-urlencode "__RequestVerificationToken=$T" $B/admin/accounts/unfreeze/$CHK
T=$(tok $A /admin/accounts/details/$VAC); curl -s -o /dev/null -b $A -c $A -d moveTo=$SAV --data-urlencode 'reason=Banner test' --data-urlencode "__RequestVerificationToken=$T" $B/admin/accounts/close/$VAC
CL=$(curl -s -b $C $B/accounts/$VAC)
has "closed banner" "$CL" "This account was closed"
has "deposit disabled on closed" "$CL" 'title="Not available on a closed account"><i class="bi bi-arrow-down-circle'
check "rename closed account -> 400" "$(code -b $C -H "$J" -d '{"nickname":"x"}' $B/api/accounts/$VAC/nickname)" 400
has "closed account history still visible" "$CL" 'class="tx-row'
T=$(tok $A /admin/accounts/details/$VAC); curl -s -o /dev/null -b $A -c $A --data-urlencode 'reason=Banner test done' --data-urlencode "__RequestVerificationToken=$T" $B/admin/accounts/reopen/$VAC

echo "== Swagger"
SW=$(curl -s $B/swagger/v1/swagger.json)
for p in '/api/accounts/{id}/transactions' '/api/accounts/{id}/number' '/api/accounts/{id}/nickname' '/api/accounts/{id}/cards/{cardId}/reveal'; do has "swagger: $p" "$SW" "\"$p\""; done
hasnt "swagger: MVC card actions hidden" "$SW" '/cards/{cardId}/freeze'
summary
