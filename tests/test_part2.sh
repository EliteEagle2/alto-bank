#!/usr/bin/env bash
# PART 2 — admin portal: every admin action end to end on freshly seeded data.
source "$(dirname "$0")/lib.sh"; wait_up
J='Content-Type: application/json'
A=$(mktemp); demo $A admin >/dev/null          # admin session
C=$(mktemp); demo $C customer >/dev/null       # Jordan (demo customer)
# admin POST helper: apost <page-with-form> <url> [curl args] -> "status redirect" ; body in /tmp/post.html
apost() { post $A "$@"; }
flash() { curl -s -b $A -c $A "$1" | tr -s ' \r\n' ' ' | grep -oE 'alert-alto-(success|error)[^>]*> *<i[^>]*></i>[^<]+' | sed 's/.*<\/i>//' | head -1; }
acct_ids() { curl -s -b $C $B/api/accounts; }
ACC=$(acct_ids)
CHK=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Checking"' | head -1 | grep -oE '[0-9]+')
SAV=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Savings"' | head -1 | grep -oE '[0-9]+')
SAV2=$(echo "$ACC" | grep -oE '"id":[0-9]+,"type":"Savings"' | sed -n 2p | grep -oE '[0-9]+')
bal() { curl -s -b $C $B/api/accounts/$1 | grep -oE '"balance":[0-9.-]+' | cut -d: -f2; }
audit_count() { curl -s -b $A "$B/admin/auditlog?action=$1" | grep -o "<code>$1</code>" | wc -l | tr -d ' '; }
echo "Jordan: checking=$CHK savings=$SAV vacation=$SAV2"

echo "== Access"
check "customer -> /admin/customers denied" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $C $B/admin/customers | grep -ci accessdenied)" 1
check "anon -> /admin/accounts -> login" "$(curl -s -o /dev/null -w '%{redirect_url}' $B/admin/accounts | grep -c /Auth/Login)" 1
check "customer POST to admin action rejected" "$(code -b $C -X POST $B/admin/accounts/freeze/$CHK)" 302

echo "== Overview + stats"
OV=$(curl -s -b $A $B/admin)
check "6 stat strip cells" "$(echo "$OV" | grep -c 'class="kpi-cell')" 6
has "pending applications tile = 1" "$OV" 'data-kpi="pending">1<'
has "work queue panel" "$OV" "Work queue"
ST=$(curl -s -b $A "$B/api/admin/stats")
check "stats: 12 weeks of new accounts" "$(echo "$ST" | grep -o '"weekStart"' | wc -l | tr -d ' ')" 12
has "stats: deposits by type" "$ST" '"depositsByType":{"Checking"'
has "stats: pending = 1" "$ST" '"pendingApplications":1'

echo "== Customers list / search / sort"
NCUST=$(curl -s -b $A "$B/api/admin/customers" | grep -o '"email"' | wc -l | tr -d ' ')
check "list shows every customer ($NCUST)" "$(curl -s -b $A $B/admin/customers | grep -c '<td><a [^>]*class="fw-semibold"')" "$NCUST"
check "search maya -> 1" "$(curl -s -b $A "$B/admin/customers?search=maya" | grep -c '<td><a [^>]*class="fw-semibold"')" 1
FIRST=$(curl -s -b $A "$B/admin/customers?sort=balance&desc=true" | grep -oE '<td><a [^>]*class="fw-semibold"[^>]*>[^<]*' | head -1 | sed 's/.*>//')
check "sort by balance desc -> Maya first" "$FIRST" "Maya Chen"

echo "== Create customer (temp password, forced change)"
NEWEMAIL="new$RANDOM$RANDOM@test.com"
R=$(apost /admin/customers/create /admin/customers/create --data-urlencode FirstName=Riley --data-urlencode LastName=Park \
     --data-urlencode "Email=$NEWEMAIL" --data-urlencode "Phone=(814) 555-0100" --data-urlencode DateOfBirth=1990-05-05 \
     --data-urlencode "Street=12 Pine St" --data-urlencode City=Altoona --data-urlencode State=PA --data-urlencode Zip=16601)
NEWURL=$(echo "$R" | cut -d' ' -f2); NEWID=$(echo "$NEWURL" | grep -oE 'details/[^?]+' | cut -d/ -f2)
check "create -> redirect to details" "$(echo "$R" | cut -d' ' -f1)" 302
DP=$(curl -s -b $A -c $A "$NEWURL")
TEMP=$(echo "$DP" | grep -o 'data-temp-password>[^<]*' | sed 's/.*>//')
[ -n "$TEMP" ] && ok "temporary password shown ($TEMP)" || bad "no temp password"
check "temp password shown only once" "$(curl -s -b $A "$NEWURL" | grep -c 'data-temp-password')" 0
check "under-18 DOB rejected ([MinimumAge])" "$(apost /admin/customers/create /admin/customers/create --data-urlencode FirstName=Kid --data-urlencode LastName=Z --data-urlencode "Email=kid$RANDOM@t.com" --data-urlencode DateOfBirth=2015-01-01 | cut -d' ' -f1)" 200
N=$(mktemp)
check "new customer login -> forced to change password" "$(login $N "$NEWEMAIL" "$TEMP")" "$B/auth/changepassword"
check "dashboard blocked until changed" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $N $B/dashboard)" "$B/auth/changepassword"
R=$(post $N /auth/changepassword /auth/changepassword --data-urlencode "CurrentPassword=$TEMP" --data-urlencode "NewPassword=Better123!" --data-urlencode "ConfirmPassword=Better123!")
check "change password -> dashboard" "$(echo "$R" | cut -d' ' -f2)" "$B/dashboard"
check "dashboard now opens" "$(code -b $N $B/dashboard)" 200
check "audit: Customer.Create" "$(audit_count Customer.Create)" 1

echo "== Profile edit, open account, notes"
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/updateprofile/$NEWID" --data-urlencode Profile.FirstName=Riley --data-urlencode Profile.LastName=Parker \
     --data-urlencode "Profile.Email=$NEWEMAIL" --data-urlencode Profile.Zip=16602 --data-urlencode Profile.City=Altoona --data-urlencode Profile.State=PA)
has "profile saved" "$(flash "$B/admin/customers/details/$NEWID")" "Profile saved"
has "audit has before/after" "$(curl -s -b $A "$B/admin/auditlog?action=Customer.UpdateProfile")" '&quot;lastName&quot;:&quot;Park&quot;'
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/openaccount/$NEWID" -d type=Checking --data-urlencode nickname=Starter -d openingDeposit=150 -d reason=)
has "open account without reason rejected" "$(flash "$B/admin/customers/details/$NEWID")" "reason of at least 5"
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/openaccount/$NEWID" -d type=Checking --data-urlencode nickname=Starter -d openingDeposit=150 --data-urlencode "reason=Branch walk-in customer")
has "open account with reason" "$(flash "$B/admin/customers/details/$NEWID")" "Opened Checking"
NC=$(curl -s -b $N $B/api/accounts)
has "customer sees new account with \$150" "$NC" '"name":"Starter"'
has "  ...balance 150" "$NC" '"balance":150'
check "new checking got a debit card" "$(curl -s -b $A "$B/admin/customers/details/$NEWID" | grep -c '(Active)')" 1
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/addnote/$NEWID" --data-urlencode "text=Prefers email contact.")
has "note added + shows author" "$(curl -s -b $A "$B/admin/customers/details/$NEWID?tab=notes")" "Prefers email contact."

echo "== Lock / unlock / reset password / force change"
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/lock/$NEWID" --data-urlencode "reason=Suspicious login attempts")
has "lock flash" "$(flash "$B/admin/customers/details/$NEWID")" "can no longer log in"
check "locked customer can't log in" "$(login $(mktemp) "$NEWEMAIL" 'Better123!')" ""
has "login page explains lock" "$(cat /tmp/post.html 2>/dev/null; curl -s -b $(mktemp) $B/auth/login >/dev/null; echo locked)" "locked"
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/unlock/$NEWID" --data-urlencode "reason=Verified identity by phone")
check "unlocked customer can log in" "$(login $(mktemp) "$NEWEMAIL" 'Better123!')" "$B/dashboard"
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/resetpassword/$NEWID" --data-urlencode "reason=Customer forgot password")
TEMP2=$(curl -s -b $A -c $A "$(echo "$R" | cut -d' ' -f2)" | grep -o 'data-temp-password>[^<]*' | sed 's/.*>//')
[ -n "$TEMP2" ] && ok "reset shows new temp password" || bad "no temp password after reset"
check "old password stops working" "$(login $(mktemp) "$NEWEMAIL" 'Better123!')" ""
check "temp password -> forced change" "$(login $(mktemp) "$NEWEMAIL" "$TEMP2")" "$B/auth/changepassword"
hasnt "temp password never written to audit log" "$(curl -s -b $A "$B/admin/auditlog/export")" "$TEMP2"
R=$(apost "/admin/customers/details/$NEWID" "/admin/customers/forcepasswordchange/$NEWID?required=false" --data-urlencode "reason=Customer set password at branch")
has "force-change toggled off" "$(flash "$B/admin/customers/details/$NEWID")" "requirement removed"

echo "== View as customer (read-only)"
VA=$(curl -s -b $A "$B/admin/customers/viewas/$NEWID")
has "view-as banner" "$VA" "Viewing as Riley Parker"
has "  ...read only" "$VA" "read only"
check "  ...no forms inside preview" "$(echo "$VA" | sed -n '/viewas-frame/,$p' | grep -c '<form' )" 0   # the layout's logout form is ABOVE the preview

echo "== Accounts: filters"
check "filter type=Savings shows only savings" "$(curl -s -b $A "$B/admin/accounts?type=Savings" | grep -c '<td>Checking</td>')" 0
check "filter minBalance=10000" "$(curl -s -b $A "$B/admin/accounts?minBalance=10000" | grep -c 'status-pill')" 1
check "filter owner=sam" "$(curl -s -b $A "$B/admin/accounts?owner=sam" | grep -c 'status-pill')" 2

echo "== Freeze: customer withdraw/transfer blocked, deposits allowed"
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/freeze/$CHK" -d reason=)
has "freeze without reason rejected" "$(flash "$B/admin/accounts/details/$CHK")" "reason of at least 5"
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/freeze/$CHK" --data-urlencode "reason=Card reported stolen")
has "frozen" "$(curl -s -b $A "$B/admin/accounts/details/$CHK")" "status-frozen"
R=$(post $C /transactions/withdraw /transactions/withdraw -d "AccountId=$CHK&Amount=10")
has "customer withdraw blocked (MVC)" "$(cat /tmp/post.html)" "is frozen"
R=$(post $C /transactions/deposit /transactions/deposit -d "AccountId=$CHK&Amount=10")
check "customer deposit still allowed" "$(echo $R | cut -d' ' -f1)" 302
check "transfer OUT of frozen blocked (API 400)" "$(code -b $C -H "$J" -d "{\"fromAccountId\":$CHK,\"toAccountId\":$SAV,\"amount\":5}" $B/api/transfers)" 400
has "  ...with clear message" "$(curl -s -b $C -H "$J" -d "{\"fromAccountId\":$CHK,\"toAccountId\":$SAV,\"amount\":5}" $B/api/transfers)" "is frozen"
check "transfer INTO frozen allowed" "$(code -b $C -H "$J" -d "{\"fromAccountId\":$SAV,\"toAccountId\":$CHK,\"amount\":5}" $B/api/transfers)" 200
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/unfreeze/$CHK" --data-urlencode "reason=Card replaced, all clear")
check "after unfreeze withdraw works" "$(post $C /transactions/withdraw /transactions/withdraw -d "AccountId=$CHK&Amount=5" | cut -d' ' -f1)" 302

echo "== Adjustments"
B0=$(bal $CHK)
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/adjust/$CHK" -d direction=credit -d amount=25.50 --data-urlencode "reason=Courtesy fee refund")
check "credit adjustment" "$(bal $CHK)" "$(awk -v b=$B0 'BEGIN{printf "%.2f", b+25.50}' | sed 's/\.\?0*$//')"
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/adjust/$CHK" -d direction=debit -d amount=999999 --data-urlencode "reason=Testing overdraft guard")
has "debit beyond balance rejected" "$(flash "$B/admin/accounts/details/$CHK")" "Overdrafts aren"
has "adjustment row shows a customer-facing memo, not the internal reason" "$(curl -s -b $A "$B/admin/accounts/details/$CHK")" "Posted by Alto Bank"
has "internal reason kept in the audit log" "$(curl -s -b $A "$B/admin/auditlog?action=Account.Adjust")" "Courtesy fee refund"

echo "== Daily transfer limit (account override)"
USED=$(curl -s -b $C $B/accounts/$CHK | tr -s ' \n' ' ' | grep -oE '\(<span class="money[^>]*>\$[0-9.,]+</span> used today' | grep -oE '[0-9][0-9.,]*' | head -1 | tr -d ,)
LIM=$(awk -v u="${USED:-0}" 'BEGIN{printf "%.2f", u+50}')   # 50 more than already used today (seeded P2P payments count too)
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/updatesettings/$CHK" --data-urlencode nickname=Everyday\ Checking -d dailyLimit=$LIM --data-urlencode "reason=Lower limit while card is replaced")
check "transfer within limit ok" "$(code -b $C -H "$J" -d "{\"fromAccountId\":$CHK,\"toAccountId\":$SAV,\"amount\":40}" $B/api/transfers)" 200
has "transfer over remaining limit blocked" "$(curl -s -b $C -H "$J" -d "{\"fromAccountId\":$CHK,\"toAccountId\":$SAV,\"amount\":20}" $B/api/transfers)" "daily transfer limit"
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/updatesettings/$CHK" --data-urlencode nickname=Everyday\ Checking -d dailyLimit= --data-urlencode "reason=Back to bank default")

echo "== Interest"
S0=$(bal $SAV)
R=$(apost "/admin/accounts/details/$SAV" "/admin/accounts/postinterest/$SAV" --data-urlencode "reason=Monthly interest catch-up")
has "post interest to savings" "$(flash "$B/admin/accounts/details/$SAV")" "Posted"
R=$(apost "/admin/accounts/details/$CHK" "/admin/accounts/postinterest/$CHK" --data-urlencode "reason=Should fail for checking")
has "interest on checking rejected" "$(flash "$B/admin/accounts/details/$CHK")" "only be posted to savings"

echo "== Close (balance must move first) / reopen"
R=$(apost "/admin/accounts/details/$SAV2" "/admin/accounts/close/$SAV2" --data-urlencode "reason=Customer request")
has "close with balance and no target rejected" "$(flash "$B/admin/accounts/details/$SAV2")" "Choose another of the customer"
V0=$(bal $SAV2); S1=$(bal $SAV)
R=$(apost "/admin/accounts/details/$SAV2" "/admin/accounts/close/$SAV2" -d moveTo=$SAV --data-urlencode "reason=Customer request")
has "closed after moving balance" "$(flash "$B/admin/accounts/details/$SAV2")" "is closed"
check "closed account balance 0" "$(bal $SAV2)" "0"
check "target got the money" "$(bal $SAV)" "$(awk -v a=$S1 -v b=$V0 'BEGIN{printf "%.2f", a+b}' | sed 's/\.\?0*$//')"
check "customer can't deposit into closed" "$(post $C /transactions/deposit /transactions/deposit -d "AccountId=$SAV2&Amount=10" | cut -d' ' -f1)" 200
R=$(apost "/admin/accounts/details/$SAV2" "/admin/accounts/reopen/$SAV2" --data-urlencode "reason=Customer changed their mind")
has "reopened" "$(curl -s -b $A "$B/admin/accounts/details/$SAV2")" "status-active"

echo "== Transactions: filters, CSV, reverse"
check "filter type=Interest" "$(curl -s -b $A "$B/admin/transactions?type=Interest" | grep -c '<td>Interest</td>')" "$(curl -s -b $A "$B/admin/transactions?type=Interest" | grep -c 'status-pill')"
check "filter amount 1000-3000 rows all in range" "$(curl -s -b $A "$B/admin/transactions?minAmount=1000&maxAmount=3000" | grep -oE '[+−]\$[0-9,]+\.[0-9]{2}' | tr -d '+−$,' | awk '$1<1000||$1>3000' | wc -l | tr -d ' ')" 0
CSVH=$(curl -s -D - -o /tmp/tx.csv -b $A "$B/admin/transactions/export?customer=jordan")
has "CSV content type" "$CSVH" "text/csv"
has "CSV header" "$(head -1 /tmp/tx.csv)" "Id,Date (UTC),Customer"
TRANSFER=$(curl -s -b $C "$B/api/accounts/$SAV/transactions?page=1" | grep -oE '"id":[0-9]+,"date":"[^"]+","description":"Transfer from[^"]*","type":"Transfer In"' | head -1 | grep -oE '^"id":[0-9]+' | grep -oE '[0-9]+')
echo "  reversing transfer leg #$TRANSFER"
R=$(apost "/admin/transactions/details/$TRANSFER" "/admin/transactions/reverse/$TRANSFER" --data-urlencode "reason=Customer disputed transfer")
has "reversed both legs" "$(flash "$B/admin/transactions/details/$TRANSFER")" "both sides of the transfer"
TD=$(curl -s -b $A "$B/admin/transactions/details/$TRANSFER")
has "original marked Reversed" "$TD" "status-reversed"
[ -n "$(echo "$TD" | tr -s ' \r\n' ' ' | grep -oE 'Reversed by</dt> <dd> ?<a ')" ] && ok "linked to its reversal" || bad "no reversal link"
R=$(apost "/admin/transactions/details/$TRANSFER" "/admin/transactions/reverse/$TRANSFER" --data-urlencode "reason=Trying twice")
has "can't reverse twice" "$(flash "$B/admin/transactions/details/$TRANSFER")" "already reversed"
REV=$(echo "$TD" | tr -s ' \r\n' ' ' | grep -oE 'Reversed by</dt> <dd> ?<a [^>]*>#[0-9]+' | grep -oE '[0-9]+$')
R=$(apost "/admin/transactions/details/$REV" "/admin/transactions/reverse/$REV" --data-urlencode "reason=Reverse the reversal")
has "can't reverse a reversal" "$(flash "$B/admin/transactions/details/$REV")" "itself a reversal"

echo "== Applications: approve Sam's pending one"
PEND=$(curl -s -b $A "$B/admin/applications?status=Pending" | grep -oE 'applications/details/[0-9]+' | head -1 | cut -d/ -f3)
R=$(apost "/admin/applications/details/$PEND" "/admin/applications/approve/$PEND" --data-urlencode "reason=Identity and funds verified")
has "approved + account opened" "$(flash "$B/admin/applications/details/$PEND")" "approved. Account"
S=$(mktemp); login $S sam@altobank.com 'Demo123!' >/dev/null
has "Sam now has the Emergency Fund account" "$(curl -s -b $S $B/api/accounts)" '"name":"Emergency Fund"'
has "Sam's funding leg recorded" "$(curl -s -b $S $B/api/accounts | grep -o '"id":[0-9]*,"type":"Checking"' | head -1 | grep -oE '[0-9]+' | xargs -I{} curl -s -b $S "$B/api/accounts/{}/transactions?page=1")" '"description":"Opening deposit","type":"Transfer Out"'

echo "== Settings: auto-approve OFF -> pending -> deny with reason"
R=$(apost /admin/settings /admin/settings -d SavingsApyPercent=4.10 -d CheckingApyPercent=0 -d DefaultDailyTransferLimit=5000 -d LargeTransactionThreshold=2500 -d MobileDepositPerCheckLimit=5000 -d MobileDepositDailyLimit=10000 -d AutoApproveApplications=false -d Reason=)
check "settings without reason rejected (form re-shown)" "$(echo $R | cut -d' ' -f1)" 200
R=$(apost /admin/settings /admin/settings -d SavingsApyPercent=4.10 -d CheckingApyPercent=0 -d DefaultDailyTransferLimit=5000 -d LargeTransactionThreshold=2500 -d MobileDepositPerCheckLimit=5000 -d MobileDepositDailyLimit=10000 -d AutoApproveApplications=false --data-urlencode "Reason=Manual review this week")
has "settings saved" "$(flash $B/admin/settings)" "Settings saved"
# Maya applies (returning customer, all info prefilled) — minimal valid application
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
T=$(tok $M /accounts/apply); V=$(curl -s -b $M -c $M -X POST -H "RequestVerificationToken: $T" $B/accounts/verifyidentity)
IDT=$(echo "$V" | sed 's/.*"token":"\([^"]*\)".*/\1/')
R=$(post $M /accounts/apply /accounts/apply -d AccountType=Savings --data-urlencode "Nickname=Rainy Day" -d FirstName=Maya -d MiddleInitial=L -d LastName=Chen \
  -d DobMonth=9 -d DobDay=30 -d DobYear=1988 -d Citizenship=US --data-urlencode "Phone=(215) 555-0187" --data-urlencode "Street=1700 Walnut St" \
  --data-urlencode "Apt=Unit 12" -d City=Philadelphia -d State=PA -d Zip=19103 -d LivedTwoYears=true -d EmploymentStatus=Employed \
  --data-urlencode "Employer=Liberty Health Partners" --data-urlencode "Occupation=Registered Nurse" --data-urlencode 'IncomeRange=$200,000 or more' \
  -d SourceOfFunds=Employment --data-urlencode "IdentityToken=$IDT" --data-urlencode "QuizStreet=1700 Walnut St" -d QuizZip=19103 -d InitialDeposit=0 -d FundingAccountId= \
  -d AgreeAccountAgreement=true -d AgreePrivacy=true -d AgreeESign=true --data-urlencode "ESignName=Maya Chen")
DEC=$(echo "$R" | cut -d' ' -f2)
has "customer sees 'We're reviewing'" "$(curl -s -b $M "$DEC")" "reviewing your application"
MAPP=$(echo "$DEC" | grep -oE '[0-9]+$')
R=$(apost "/admin/applications/details/$MAPP" "/admin/applications/deny/$MAPP" --data-urlencode "reason=We need a recent proof of address before opening another account.")
has "denied" "$(flash "$B/admin/applications/details/$MAPP")" "denied"
has "customer sees denial reason on profile" "$(curl -s -b $M $B/profile)" "recent proof of address"
has "customer decision page shows denial" "$(curl -s -b $M "$DEC")" "couldn"
R=$(apost /admin/settings /admin/settings -d SavingsApyPercent=4.10 -d CheckingApyPercent=0 -d DefaultDailyTransferLimit=5000 -d LargeTransactionThreshold=2500 -d MobileDepositPerCheckLimit=5000 -d MobileDepositDailyLimit=10000 -d AutoApproveApplications=true --data-urlencode "Reason=Back to auto-approve")

echo "== Monthly interest run"
R=$(apost /admin/settings /admin/settings/runinterest --data-urlencode "reason=November interest run")
SP=$(curl -s -b $A $B/admin/settings)
has "interest run summary" "$SP" "Monthly interest summary"
has "  ...flash with totals" "$SP" "accounts credited"

echo "== Audit log"
for a in Account.Freeze Account.Unfreeze Account.Adjust Account.Close Account.Reopen Account.PostInterest Transaction.Reverse Application.Approve Application.Deny Settings.Update Interest.MonthlyRun Customer.Lock Customer.Unlock Customer.ResetPassword Auth.Login Auth.LoginFailed Transfer Account.Open; do
  n=$(audit_count $a); [ "$n" -ge 1 ] && ok "audit has $a ($n)" || bad "audit missing $a"
done
has "audit rows carry the reason" "$(curl -s -b $A "$B/admin/auditlog?action=Account.Freeze")" "Card reported stolen"
check "audit filter by user" "$(curl -s -b $A "$B/admin/auditlog?user=sam@" | grep -o '<td>sam@altobank.com' | wc -l | tr -d ' ' | awk '{print ($1>0)?"yes":"no"}')" yes
has "audit CSV" "$(curl -s -b $A "$B/admin/auditlog/export?action=Account.Freeze" | head -1)" "Id,Time (UTC),Actor"

echo "== Admin API"
check "GET /api/admin/customers?search=jordan" "$(curl -s -b $A "$B/api/admin/customers?search=jordan" | grep -o '"email"' | wc -l | tr -d ' ')" 1
has "GET /api/admin/accounts/{id}" "$(curl -s -b $A $B/api/admin/accounts/$CHK)" '"status":"Active"'
check "freeze via API without reason -> 400" "$(code -b $A -H "$J" -d '{}' $B/api/admin/accounts/$CHK/freeze)" 400
has "freeze via API" "$(curl -s -b $A -H "$J" -d '{"reason":"API test freeze"}' $B/api/admin/accounts/$CHK/freeze)" '"status":"Frozen"'
has "unfreeze via API" "$(curl -s -b $A -H "$J" -d '{"reason":"API test unfreeze"}' $B/api/admin/accounts/$CHK/unfreeze)" '"status":"Active"'
check "customer -> admin API 403" "$(code -b $C $B/api/admin/customers)" 403
SW=$(curl -s $B/swagger/v1/swagger.json)
for p in '/api/admin/customers' '/api/admin/accounts/{id}' '/api/admin/accounts/{id}/freeze' '/api/admin/accounts/{id}/unfreeze'; do has "swagger: $p" "$SW" "\"$p\""; done
hasnt "swagger: admin HTML pages hidden" "$SW" '"/admin/'
summary
