#!/usr/bin/env bash
# FEATURE 1 — Send & Request (P2P payments, requests, split a bill) + the Alerts center.
# Needs B (test server) and TEST_DB (its scratch database, used to age a request past 14 days).
source "$(dirname "$0")/lib.sh"; wait_up
J='Content-Type: application/json'
C=$(mktemp); demo $C customer >/dev/null                    # Jordan
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
S=$(mktemp); login $S sam@altobank.com 'Demo123!' >/dev/null
A=$(mktemp); demo $A admin >/dev/null
apost() { post $A "$@"; }
ids() { curl -s -b $1 $B/api/accounts; }
first_id() { ids $1 | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
bal() { awk -v a="$(curl -s -b $1 $B/api/accounts/$2 | grep -oE '"balance":[0-9.-]+' | cut -d: -f2)" 'BEGIN{printf "%.2f", a}'; }
add() { awk -v a="$1" -v b="$2" 'BEGIN{printf "%.2f", a+b}'; }
num() { awk -v a="$1" 'BEGIN{printf "%.2f", a}'; }
unread() { curl -s -b $1 $B/dashboard | grep -oE 'data-unread="[0-9]+"' | grep -oE '[0-9]+' || echo 0; }
send() {   # send <jar> <fromId> <to> <amount> [memo] -> flash text (two-step: review then confirm)
  local jar=$1; post $jar /payments /payments/send/review --data-urlencode "Send.FromAccountId=$2" --data-urlencode "Send.To=$3" -d "Send.Amount=$4" --data-urlencode "Send.Memo=${5:-}" >/dev/null
  local REVIEW=$(cat /tmp/post.html)
  if printf '%s' "$REVIEW" | grep -q 'Sending to'; then
    local T=$(printf '%s' "$REVIEW" | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | head -1 | sed 's/.*value="//;s/"$//')
    curl -s -b $jar -c $jar -o /dev/null --data-urlencode "Input.FromAccountId=$2" --data-urlencode "Input.To=$3" -d "Input.Amount=$4" --data-urlencode "Input.Memo=${5:-}" --data-urlencode "__RequestVerificationToken=$T" $B/payments/send
  fi
  flash $jar $B/payments
}
reqs() { curl -s -b $1 $B/api/payments/requests; }
req_status() { reqs $1 | grep -oE "\"id\":$2,[^}]*" | grep -oE '"status":"[A-Za-z]+"' | cut -d'"' -f4; }

JCHK=$(first_id $C Checking); JVAC=$(first_id $C Savings 2)
MCHK=$(first_id $M Checking); SCHK=$(first_id $S Checking)
echo "Jordan checking=$JCHK vacation=$JVAC; Maya checking=$MCHK; Sam checking=$SCHK"

echo "== Access"
check "anon /payments -> login" "$(curl -s -o /dev/null -w '%{redirect_url}' $B/payments | grep -c /Auth/Login)" 1
check "anon API -> 401" "$(code $B/api/payments/requests)" 401
check "admin -> customer API 403" "$(code -b $A $B/api/payments/requests)" 403
check "admin -> /payments denied" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $A $B/payments | grep -ci accessdenied)" 1
P=$(curl -s -b $C $B/payments)
has "sidebar link: Send & Request" "$P" 'href="/payments"'
has "page: Send / Request / Inbox / Activity tabs" "$(echo "$P" | grep -oE 'data-bs-target="#(send|request|inbox|activity)"' | sort -u | wc -l | tr -d ' ')" 4
has "seeded: Maya's pizza request in Jordan's inbox" "$P" "Pizza night"
has "seeded: Samuel's split bill in Jordan's inbox" "$P" "Utilities - October"
has "topbar bell with unread count" "$P" 'class="bell-count"'

echo "== Recipient preview (GET /api/payments/recipient)"
has "by email -> Maya C." "$(curl -s -b $C "$B/api/payments/recipient?q=maya@altobank.com")" '"displayName":"Maya C."'
has "by phone (formatted) -> Samuel O." "$(curl -s -b $C "$B/api/payments/recipient?q=%28717%29%20555-0119")" '"displayName":"Samuel O."'
has "by phone (+1 digits) -> Samuel O." "$(curl -s -b $C "$B/api/payments/recipient?q=17175550119")" 'Samuel O.'
has "email is case-insensitive" "$(curl -s -b $C "$B/api/payments/recipient?q=MAYA@AltoBank.com")" 'Maya C.'
check "yourself -> 400" "$(code -b $C "$B/api/payments/recipient?q=demo@altobank.com")" 400
check "unknown -> 404" "$(code -b $C "$B/api/payments/recipient?q=nobody@example.com")" 404
check "an admin is not a recipient -> 404" "$(code -b $C "$B/api/payments/recipient?q=admin@altobank.com")" 404
hasnt "preview never leaks a full last name" "$(curl -s -b $C "$B/api/payments/recipient?q=maya@altobank.com")" 'Chen'

echo "== Send (two steps)"
J0=$(bal $C $JCHK); M0=$(bal $M $MCHK); MU0=$(unread $M)
post $C /payments /payments/send/review --data-urlencode "Send.FromAccountId=$JCHK" --data-urlencode "Send.To=maya@altobank.com" -d "Send.Amount=12.34" --data-urlencode "Send.Memo=Coffee run" >/dev/null
has "review shows 'Sending to Maya C.'" "$(cat /tmp/post.html)" 'Sending to Maya C.'
check "review moves no money" "$(bal $C $JCHK)" "$J0"
check "review without antiforgery token -> 400" "$(code -b $C -d "Send.FromAccountId=$JCHK&Send.To=maya@altobank.com&Send.Amount=1" $B/payments/send/review)" 400
has "send succeeds" "$(send $C $JCHK maya@altobank.com 12.34 'Coffee run')" "Sent \$12.34 to Maya C."
check "sender balance down" "$(bal $C $JCHK)" "$(add $J0 -12.34)"
check "recipient (primary checking) balance up" "$(bal $M $MCHK)" "$(add $M0 12.34)"
JT=$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?pageSize=1")
has "sender row: PaymentSent" "$JT" '"kind":"PaymentSent"'
has "sender row: counterparty Maya C." "$JT" '"counterparty":"Maya C."'
MT=$(curl -s -b $M "$B/api/accounts/$MCHK/transactions?pageSize=1")
has "recipient row: PaymentReceived" "$MT" '"kind":"PaymentReceived"'
has "recipient row: counterparty Jordan R." "$MT" '"counterparty":"Jordan R."'
has "recipient row: memo" "$MT" '"memo":"Coffee run"'
has "row has bank date for Today/Yesterday grouping" "$MT" '"bankDate":"'
check "recipient gets a 'Money received' alert" "$(unread $M)" "$((MU0 + 1))"
has "alert text" "$(curl -s -b $M $B/alerts)" 'Jordan R. sent you $12.34'
has "payment filter on activity" "$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?kind=payment&pageSize=50")" '"kind":"PaymentSent"'
hasnt "payment filter excludes others" "$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?kind=payment&pageSize=50")" '"kind":"Withdrawal"'
has "account page row shows counterparty" "$(curl -s -b $C $B/accounts/$JCHK)" 'Maya C.'

echo "== Send is refused when it should be"
has "to yourself" "$(send $C $JCHK demo@altobank.com 5)" "send money to yourself"
has "unknown recipient" "$(send $C $JCHK ghost@example.com 5)" "find an Alto customer"
has "insufficient funds" "$(send $C $JVAC maya@altobank.com $(add $(bal $C $JVAC) 1))" "Insufficient funds"
has "someone else's account as source" "$(send $C $MCHK sam@altobank.com 1)" "Account not found"
check "API: zero amount -> 400" "$(code -b $C -H "$J" -d "{\"fromAccountId\":$JCHK,\"to\":\"maya@altobank.com\",\"amount\":0}" $B/api/payments/send)" 400
check "API: 3 decimals -> 400" "$(code -b $C -H "$J" -d "{\"fromAccountId\":$JCHK,\"to\":\"maya@altobank.com\",\"amount\":1.234}" $B/api/payments/send)" 400
check "API: form-encoded (CSRF) -> 415" "$(code -b $C -d "fromAccountId=$JCHK&to=maya@altobank.com&amount=1" $B/api/payments/send)" 415
check "API: other user's source account -> 400" "$(code -b $C -H "$J" -d "{\"fromAccountId\":$MCHK,\"to\":\"sam@altobank.com\",\"amount\":1}" $B/api/payments/send)" 400

echo "== Frozen / limits enforced in BankService"
JU0=$(unread $C)
apost "/admin/accounts/details/$JCHK" "/admin/accounts/freeze/$JCHK" --data-urlencode "reason=Testing P2P freeze" >/dev/null
check "freeze creates an 'Account frozen' alert" "$(unread $C)" "$((JU0 + 1))"
has "frozen sender can't send" "$(send $C $JCHK maya@altobank.com 1)" "frozen"
has "frozen sender can't send (API)" "$(curl -s -b $C -H "$J" -d "{\"fromAccountId\":$JCHK,\"to\":\"maya@altobank.com\",\"amount\":1}" $B/api/payments/send)" "frozen"
apost "/admin/accounts/details/$JCHK" "/admin/accounts/unfreeze/$JCHK" --data-urlencode "reason=Done testing" >/dev/null
has "unfreeze creates an 'Account unfrozen' alert" "$(curl -s -b $C $B/alerts)" 'Account unfrozen'
apost "/admin/accounts/details/$SCHK" "/admin/accounts/freeze/$SCHK" --data-urlencode "reason=Testing recipient freeze" >/dev/null
has "recipient whose only checking is frozen can't receive" "$(send $C $JCHK sam@altobank.com 1)" "receive payments right now"
check "preview says so too" "$(code -b $C "$B/api/payments/recipient?q=sam@altobank.com")" 404
apost "/admin/accounts/details/$SCHK" "/admin/accounts/unfreeze/$SCHK" --data-urlencode "reason=Done testing" >/dev/null
used() { curl -s -b $C $B/accounts/$JCHK | tr -s ' \n' ' ' | grep -oE '\(<span class="money[^>]*>\$[0-9.,]+</span> used today' | grep -oE '[0-9][0-9.,]*' | head -1 | tr -d ,; }
U0=$(used); send $C $JCHK maya@altobank.com 1.00 'limit check' >/dev/null; U1=$(used)
check "payments count toward the daily limit" "$U1" "$(add $U0 1)"
apost "/admin/accounts/details/$JCHK" "/admin/accounts/updatesettings/$JCHK" --data-urlencode "nickname=Everyday Checking" -d dailyLimit=100 --data-urlencode "reason=Testing P2P limit" >/dev/null
LEFT=$(awk -v u="$U1" 'BEGIN{printf "%.2f", 100-u}')
has "a payment over the remaining limit is refused" "$(send $C $JCHK maya@altobank.com $(add $LEFT 0.01))" "daily transfer limit"
has "a payment within it works" "$(send $C $JCHK maya@altobank.com $(add $LEFT -1))" "Sent [$]"
JSAV=$(first_id $C Savings 1)
has "and transfers share the same limit" "$(curl -s -b $C -H "$J" -d "{\"fromAccountId\":$JCHK,\"toAccountId\":$JSAV,\"amount\":10}" $B/api/transfers)" "daily transfer limit"
apost "/admin/accounts/details/$JCHK" "/admin/accounts/updatesettings/$JCHK" --data-urlencode "nickname=Everyday Checking" -d dailyLimit= --data-urlencode "reason=Back to default" >/dev/null

echo "== Racing payments can't overdraw"
V0=$(bal $C $JVAC); AMT=$(awk -v b="$V0" 'BEGIN{printf "%.2f", int(b*0.4*100)/100}')
for i in 1 2 3 4; do curl -s -o /tmp/race_p2p_$i.json -b $C -H "$J" -d "{\"fromAccountId\":$JVAC,\"to\":\"maya@altobank.com\",\"amount\":$AMT,\"memo\":\"race $i\"}" $B/api/payments/send & done; wait
OK=$(grep -l '"direction":"sent"' /tmp/race_p2p_*.json | wc -l | tr -d ' ')
V1=$(bal $C $JVAC)
check "at most 2 of 4 racing payments succeed" "$([ "$OK" -le 2 ] && echo yes)" yes
check "balance = start - successes x amount (no lost update)" "$(num $V1)" "$(awk -v b="$V0" -v n="$OK" -v a="$AMT" 'BEGIN{printf "%.2f", b-n*a}')"
check "never negative" "$(awk -v b="$V1" 'BEGIN{print (b>=0)?"ok":"neg"}')" ok

echo "== API send"
R=$(curl -s -b $C -H "$J" -d "{\"fromAccountId\":$JCHK,\"to\":\"(717) 555-0119\",\"amount\":2.50,\"memo\":\"api\"}" $B/api/payments/send)
has "API send -> PaymentDto" "$R" '"direction":"sent","counterparty":"Samuel O.","amount":2.5'
hasnt "API DTO has no user ids" "$R" 'UserId'

echo "== Requests"
SU0=$(unread $S)
post $C /payments /payments/request --data-urlencode "Request.People[0].To=sam@altobank.com" -d "Request.People[0].Amount=20" --data-urlencode "Request.Memo=Snacks" -d "Request.Mode=single" >/dev/null
has "single request" "$(flash $C $B/payments)" 'Request sent to Samuel O. ($20.00)'
RID=$(reqs $S | grep -oE '"id":[0-9]+,"direction":"incoming","counterparty":"Jordan R.","amount":20,"memo":"Snacks"' | grep -oE '[0-9]+' | head -1)
check "payer sees it (incoming)" "$([ -n "$RID" ] && echo yes)" yes
check "payer alerted" "$(unread $S)" "$((SU0 + 1))"
check "status Pending" "$(req_status $S $RID)" Pending
check "requester can't pay their own request -> 404" "$(code -b $C -H "$J" -d "{\"fromAccountId\":$JCHK}" $B/api/payments/requests/$RID/pay)" 404
check "a third person can't pay it -> 404" "$(code -b $M -H "$J" -d "{\"fromAccountId\":$MCHK}" $B/api/payments/requests/$RID/pay)" 404
check "a third person can't decline it -> 404" "$(code -b $M -H "$J" -X POST $B/api/payments/requests/$RID/decline)" 404
check "payer can't cancel (only the requester) -> 404" "$(code -b $S -H "$J" -X POST $B/api/payments/requests/$RID/cancel)" 404
check "decline without JSON content type -> 415" "$(code -b $S -X POST $B/api/payments/requests/$RID/decline)" 415
S0=$(bal $S $SCHK); J1=$(bal $C $JCHK); JU1=$(unread $C)
post $S /payments "/payments/requests/$RID/pay" -d "fromAccountId=$SCHK" >/dev/null
has "pay request" "$(flash $S $B/payments)" 'Paid $20.00 to Jordan R.'
check "payer balance down" "$(bal $S $SCHK)" "$(add $S0 -20)"
check "requester balance up (primary checking)" "$(bal $C $JCHK)" "$(add $J1 20)"
check "status Paid" "$(req_status $C $RID)" Paid
check "requester alerted ('Request paid')" "$(unread $C)" "$((JU1 + 2))"   # money received + request paid
has "paying twice is refused" "$(curl -s -b $S -H "$J" -d "{\"fromAccountId\":$SCHK}" $B/api/payments/requests/$RID/pay)" "already paid"

echo "== Split a bill"
post $C /payments /payments/request --data-urlencode "Request.People[0].To=maya@altobank.com" -d "Request.People[0].Amount=1" --data-urlencode "Request.People[1].To=sam@altobank.com" -d "Request.People[1].Amount=1" -d "Request.Mode=even" -d "Request.SplitTotal=100" -d "Request.IncludeMe=true" -d "Request.IncludeMe=false" --data-urlencode "Request.Memo=Dinner split" >/dev/null
has "even split incl. me: 100 / 3 = 33.33 each (server ignores typed amounts)" "$(flash $C $B/payments)" 'Maya C. ($33.33), Samuel O. ($33.33)'
has "split shows the bill total to the payer" "$(reqs $M)" '"memo":"Dinner split","status":"Pending","createdAt":"[^"]*","expiresAt":"[^"]*","splitTotal":100'
post $C /payments /payments/request --data-urlencode "Request.People[0].To=maya@altobank.com" --data-urlencode "Request.People[1].To=sam@altobank.com" -d "Request.Mode=even" -d "Request.SplitTotal=0.05" -d "Request.IncludeMe=false" --data-urlencode "Request.Memo=Cents" >/dev/null
has "even split without me: leftover cent goes to the first person" "$(flash $C $B/payments)" 'Maya C. ($0.03), Samuel O. ($0.02)'
post $C /payments /payments/request --data-urlencode "Request.People[0].To=maya@altobank.com" -d "Request.People[0].Amount=60" --data-urlencode "Request.People[1].To=sam@altobank.com" -d "Request.People[1].Amount=50" -d "Request.Mode=custom" -d "Request.SplitTotal=100" >/dev/null
has "custom amounts can't exceed the bill" "$(flash $C $B/payments)" "more than the"
post $C /payments /payments/request --data-urlencode "Request.People[0].To=maya@altobank.com" -d "Request.People[0].Amount=5" --data-urlencode "Request.People[1].To=MAYA@altobank.com" -d "Request.People[1].Amount=5" -d "Request.Mode=single" >/dev/null
has "same person twice is refused" "$(flash $C $B/payments)" "on the list twice"
post $C /payments /payments/request --data-urlencode "Request.People[0].To=demo@altobank.com" -d "Request.People[0].Amount=5" -d "Request.Mode=single" >/dev/null
has "can't request from yourself" "$(flash $C $B/payments)" "yourself"
SIX=$(for i in 0 1 2 3 4 5; do printf -- '-d Request.People[%s].To=maya%s@altobank.com -d Request.People[%s].Amount=1 ' $i $i $i; done)
post $C /payments /payments/request $SIX -d Request.Mode=single >/dev/null
has "at most 5 people" "$(flash $C $B/payments)" "at most 5 people"
R=$(curl -s -b $C -H "$J" -d '{"recipients":[{"to":"maya@altobank.com","amount":0},{"to":"sam@altobank.com","amount":0}],"memo":"API split","splitTotal":45,"splitEvenly":true,"includeMe":true}' $B/api/payments/request)
check "API split: 2 requests of 15.00" "$(echo "$R" | grep -o '"amount":15' | wc -l | tr -d ' ')" 2

echo "== Decline / cancel"
DID=$(curl -s -b $C -H "$J" -d '{"recipients":[{"to":"maya@altobank.com","amount":7}],"memo":"To decline"}' $B/api/payments/request | grep -oE '"id":[0-9]+' | head -1 | cut -d: -f2)
JU2=$(unread $C)
post $M /payments "/payments/requests/$DID/decline" >/dev/null
has "payer declines" "$(flash $M $B/payments)" "Request declined"
check "status Declined" "$(req_status $C $DID)" Declined
check "requester alerted" "$(unread $C)" "$((JU2 + 1))"
has "can't pay a declined request" "$(curl -s -b $M -H "$J" -d "{\"fromAccountId\":$MCHK}" $B/api/payments/requests/$DID/pay)" "already declined"
CID=$(curl -s -b $C -H "$J" -d '{"recipients":[{"to":"sam@altobank.com","amount":9}],"memo":"To cancel"}' $B/api/payments/request | grep -oE '"id":[0-9]+' | head -1 | cut -d: -f2)
has "requester cancels (API)" "$(curl -s -b $C -H "$J" -X POST $B/api/payments/requests/$CID/cancel)" '"success":true'
check "status Cancelled" "$(req_status $S $CID)" Cancelled
has "payer told it's cancelled" "$(curl -s -b $S $B/alerts)" "cancelled their request"
has "can't pay a cancelled request" "$(curl -s -b $S -H "$J" -d "{\"fromAccountId\":$SCHK}" $B/api/payments/requests/$CID/pay)" "already cancelled"

echo "== Expiry after 14 days"
EID=$(curl -s -b $C -H "$J" -d '{"recipients":[{"to":"maya@altobank.com","amount":4}],"memo":"Old request"}' $B/api/payments/request | grep -oE '"id":[0-9]+' | head -1 | cut -d: -f2)
has "new request expires 14 days out" "$(reqs $C | grep -oE "\"id\":$EID,[^}]*")" '"expiresAt":"'
sql "UPDATE MoneyRequests SET CreatedAt = datetime('now','-15 days'), ExpiresAt = datetime('now','-1 days') WHERE Id = $EID" >/dev/null
has "pay after expiry is refused" "$(curl -s -b $M -H "$J" -d "{\"fromAccountId\":$MCHK}" $B/api/payments/requests/$EID/pay)" "expired"
curl -s -b $C -o /dev/null $B/payments    # page load runs the expiry sweep (the scheduler does it every minute)
check "marked Expired in the database" "$(sql "SELECT Status FROM MoneyRequests WHERE Id = $EID")" Expired
has "requester alerted ('Request expired')" "$(curl -s -b $C $B/alerts)" "Request expired"
check "expiry alert only once" "$(curl -s -b $C -o /dev/null $B/payments; sql "SELECT COUNT(*) FROM Alerts WHERE DedupeKey = 'request-expired:$EID'")" 1

echo "== Alerts center"
U=$(unread $C)
FIRST=$(curl -s -b $C "$B/alerts?show=unread" | grep -oE 'data-alert-id="[0-9]+"' | head -1 | grep -oE '[0-9]+')
post $C /alerts "/alerts/$FIRST/read" >/dev/null
check "mark one read" "$(unread $C)" "$((U - 1))"
MAYA_ALERT=$(curl -s -b $M $B/alerts | grep -oE 'data-alert-id="[0-9]+"' | head -1 | grep -oE '[0-9]+')
check "someone else's alert -> 404" "$(post $C /alerts "/alerts/$MAYA_ALERT/read" | cut -d' ' -f1)" 404
LINKED=$(curl -s -b $M "$B/alerts" | tr -s ' \n' ' ' | grep -oE 'action="/alerts/[0-9]+/read\?open=true"' | head -1 | grep -oE '[0-9]+')
check "View opens the alert's page" "$(post $M /alerts "/alerts/$LINKED/read?open=true" | cut -d' ' -f2 | grep -cE '/(payments|accounts)')" 1
post $C /alerts /alerts/read-all >/dev/null
check "mark all read" "$(unread $C)" 0
hasnt "no badge when nothing unread" "$(curl -s -b $C $B/dashboard)" 'class="bell-count"'
has "unread filter shows the empty state" "$(curl -s -b $C "$B/alerts?show=unread")" "No unread alerts"

echo "== Admin: P2P Payments page"
AP=$(curl -s -b $A $B/admin/payments)
has "sidebar link" "$AP" 'href="/admin/payments"'
has "lists payments with full names" "$AP" "Jordan Rivera"
has "KPI tiles" "$AP" "Open requests"
has "filter by customer" "$(curl -s -b $A "$B/admin/payments?customer=Okafor")" "Samuel Okafor"
hasnt "filter excludes others" "$(curl -s -b $A "$B/admin/payments?customer=nobody-matches")" "Jordan Rivera"
has "requests view with status filter" "$(curl -s -b $A "$B/admin/payments?view=requests&requestStatus=Declined")" "To decline"
check "customer can't open it" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $C $B/admin/payments | grep -ci accessdenied)" 1
PTX=$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?kind=payment&pageSize=50" | grep -oE '"id":[0-9]+,"date":"[^"]*","description":"Payment to Maya C. - Coffee run"' | grep -oE '"id":[0-9]+' | cut -d: -f2)
M1=$(bal $M $MCHK)
apost "/admin/transactions/details/$PTX" "/admin/transactions/reverse/$PTX" --data-urlencode "reason=Customer sent to wrong person" >/dev/null
has "reversing a payment uses the existing reversal" "$(curl -s -b $A $B/admin/transactions/details/$PTX)" "Reversed"
check "recipient's leg reversed too (balance back)" "$(bal $M $MCHK)" "$(add $M1 -12.34)"
has "payment marked Reversed on the P2P page" "$(curl -s -b $A "$B/admin/payments?paymentStatus=Reversed")" "Coffee run"
check "audit: Payment.Send rows" "$(curl -s -b $A "$B/admin/auditlog?action=Payment.Send" | grep -c '<code>Payment.Send</code>' | awk '{print ($1>0)?"yes":"no"}')" yes

summary
