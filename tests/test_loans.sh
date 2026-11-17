#!/usr/bin/env bash
# LOANS (personal and auto) — the seed, the public calculator (amortization checked to the cent against an independent
# exact-math schedule, tests/amortize.js), the SIMULATED decision through the real application (offer / pending / denied),
# the offer (Truth-in-Lending box, accept → money on both sides, decline, expiry, one at a time), admin approve as offered
# and counter-offer, payments (scheduled, extra principal shortens the schedule, payoff quote with interest to today,
# the final payment hits $0.00), late fee + overdue after the grace days + admin waive, autopay success and failure,
# admin adjustments and guards, loans kept out of deposit flows, the dashboard's net worth, ownership 404s, the API.
# Needs B and TEST_DB ("moves time" on the scratch DB: due dates, offer dates).
source "$(dirname "$0")/lib.sh"; wait_up
HERE="$(cd "$(dirname "$0")" && pwd)"
J=$(mktemp); demo $J customer >/dev/null                     # Jordan (auto loan partway, autopay on; a paid-off loan)
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null  # Maya (overdue personal loan)
S=$(mktemp); login $S sam@altobank.com 'Demo123!' >/dev/null   # Samuel (auto loan application waiting for review)
A=$(mktemp); demo $A admin >/dev/null
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
bal() { sql "SELECT printf('%.2f', Balance) FROM Accounts WHERE Id = $1"; }
lv() { sql "SELECT $2 FROM Loans WHERE AccountId = $1"; }
loanof() { sql "SELECT a.Id FROM Accounts a JOIN Loans l ON l.AccountId = a.Id WHERE a.UserId = '$1' AND l.Kind = '$2' AND l.Status = '$3' LIMIT 1"; }
chk() { sql "SELECT Id FROM Accounts WHERE UserId = '$1' AND AccountType = 'Checking' AND Status <> 'Closed' ORDER BY Id LIMIT 1"; }
# The loan's balance must always equal -(principal + fees).
inv() { check "$1: balance = -(principal + fees)" "$(sql "SELECT COUNT(*) FROM Loans l JOIN Accounts a ON a.Id = l.AccountId WHERE l.AccountId = $2 AND ABS(CAST(a.Balance AS REAL) + CAST(l.PrincipalRemaining AS REAL) + CAST(l.FeesOwed AS REAL)) < 0.005")" 1; }
waitfor() { local i v; for i in $(seq 1 40); do v=$(sql "$2"); [ "$v" = "$3" ] && { ok "$1 ($v)"; return; }; sleep 0.5; done; bad "$1: got [$v] expected [$3]"; }
pay() { local J=$1 L=$2; shift 2; post $J /loans/$L/pay /loans/$L/pay "$@"; }
apost() { post $A "$@"; }
d() { date -d "$TODAY $1" +%F; }
money() { awk "BEGIN { printf \"%.2f\", $1 }"; }   # money "8000 - 254.36" -> 7745.64
fmt() { node -e "console.log('\$' + Number(process.argv[1]).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }))" "$1"; }   # 1234.5 -> $1,234.50
JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM); SID=$(uid SAM@ALTOBANK.COM)
JAUTO=$(loanof $JID Auto Active); JOLD=$(loanof $JID Personal PaidOff); MLOAN=$(loanof $MID Personal Active)
JCHK=$(chk $JID); MCHK=$(chk $MID); SCHK=$(chk $SID)
TODAY=$(curl -s -b $J $B/accounts/$JAUTO | grep -o 'data-today="[0-9-]*"' | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}')
echo "bank today: $TODAY  (Jordan auto $JAUTO, paid off $JOLD; Maya $MLOAN)"

echo "== Seeded demo data"
check "three seeded loans" "$(sql "SELECT COUNT(*) FROM Loans")" 3
check "Jordan's auto loan: partway (6 of 60 paid), active, autopay from checking" "$(sql "SELECT PaymentsMade || '/' || TermMonths || ':' || Status || ':' || AutopayEnabled || ':' || AutopayFromAccountId FROM Loans WHERE AccountId = $JAUTO")" "6/60:Active:1:$JCHK"
check "Jordan's next payment is still ahead" "$(sql "SELECT NextDueDate > '$TODAY' FROM Loans WHERE AccountId = $JAUTO")" 1
check "Maya's loan: overdue (15 days late, grace 10)" "$(sql "SELECT julianday('$TODAY') - julianday(NextDueDate) FROM Loans WHERE AccountId = $MLOAN" | cut -d. -f1)" 15
check "Maya: one late fee, owed on the loan" "$(sql "SELECT COUNT(*) || ':' || (SELECT printf('%.2f', FeesOwed) FROM Loans WHERE AccountId = $MLOAN) FROM Transactions WHERE AccountId = $MLOAN AND Type = 'LateFee'")" "1:35.00"
check "Jordan's old personal loan: paid off, account closed, $0" "$(sql "SELECT l.Status || ':' || a.Status || ':' || printf('%.2f', a.Balance) FROM Loans l JOIN Accounts a ON a.Id = l.AccountId WHERE l.AccountId = $JOLD")" "PaidOff:Closed:0.00"
check "the paid-off loan ended with a payoff" "$(sql "SELECT Kind FROM LoanPayments WHERE LoanId = (SELECT Id FROM Loans WHERE AccountId = $JOLD) ORDER BY PaidAt DESC LIMIT 1")" "Payoff"
check "Samuel: a pending auto loan application (large, so staff review it)" "$(sql "SELECT Status || ':' || LoanKind || ':' || (LoanAprPercent IS NOT NULL) FROM AccountApplications WHERE UserId = '$SID' AND AccountType = 'Loan'")" "Pending:Auto:1"
for L in $JAUTO $MLOAN $JOLD; do inv "seeded loan $L" $L; done
check "every seeded payment splits exactly (interest + principal + fees = amount)" "$(sql "SELECT COUNT(*) FROM LoanPayments WHERE ABS(CAST(Interest AS REAL) + CAST(Principal AS REAL) + CAST(Fees AS REAL) - CAST(Amount AS REAL)) > 0.005")" 0
check "settings defaults: tiers, auto discount, review amount, late fee, grace days" "$(sql "SELECT LoanRateTierA || '|' || LoanRateTierB || '|' || LoanRateTierC || '|' || LoanRateTierD || '|' || AutoLoanRateDiscount || '|' || LoanReviewAmount || '|' || LoanLateFee || '|' || LoanGraceDays FROM BankSettings")" "8.99|11.99|15.99|19.99|2.5|20000|35|10"

echo "== Calculator: amortization math (independent exact-integer schedule, tests/amortize.js)"
calc() { curl -s "$B/api/loans/calculator?amount=$1&apr=$2&months=$3"; }
Q=$(calc 20000 6 60)
check "known example: \$20,000 at 6% for 60 months = \$386.66 a month" "$(json_field "$Q" monthlyPayment)" "386.66"
check "known example: total interest to the cent" "$(json_field "$Q" totalInterest)" "3199.35"
check "known example: the final payment absorbs the rounding" "$(json_field "$Q" finalPayment)" "386.41"
for c in "20000 6 60" "1000 8.99 12" "24999 19.99 60" "50000 6.49 72" "5000 0 24" "7777 13.37 36" "25000 19.99 12"; do
  set -- $c
  Q=$(calc $1 $2 $3)
  API="$(json_field "$Q" monthlyPayment) $(json_field "$Q" finalPayment) $(json_field "$Q" totalInterest) $(json_field "$Q" totalOfPayments) $(printf '%s' "$Q" | grep -oE '"balanceAfter":[0-9.]+' | tail -1 | cut -d: -f2) $(printf '%s' "$Q" | grep -o '"number"' | wc -l | tr -d ' ')"
  check "\$$1 at $2% for $3 months: payment, final, interest, total, last balance, count" "$(echo $API | awk '{ printf "%.2f %.2f %.2f %.2f %.2f %d", $1, $2, $3, $4, $5, $6 }')" "$(node "$HERE/amortize.js" $1 $2 $3)"
done
check "the schedule's principal adds up to the amount" "$(calc 7777 13.37 36 | grep -oE '"principal":[0-9.]+' | cut -d: -f2 | awk '{ s += $1 } END { printf "%.2f", s }')" "7777.00"
check "calculator rejects 0 amount" "$(code "$B/api/loans/calculator?amount=0&apr=6&months=60")" 400
check "calculator rejects APR over 36%" "$(code "$B/api/loans/calculator?amount=1000&apr=40&months=60")" 400
check "calculator rejects 0 months" "$(code "$B/api/loans/calculator?amount=1000&apr=6&months=0")" 400
check "calculator works signed out" "$(code "$B/api/loans/calculator?amount=1000&apr=6&months=12")" 200
PL=$(curl -s $B/products/loans)
has "product page: the calculator calls the API" "$PL" "/api/loans/calculator"
has "product page: amortization chart" "$PL" "data-calc-chart"
has "product page: full schedule table (text alternative)" "$PL" "data-calc-rows"
has "product page: apply for a personal loan" "$PL" "type=Loan&amp;kind=Personal"
has "product page: apply for an auto loan" "$PL" "type=Loan&amp;kind=Auto"
has "product page: demo note" "$PL" "Demo decision, no credit check"
has "product page: rates from settings" "$PL" "8.99%"

echo "== Loan page (customer)"
P=$(curl -s -b $J $B/accounts/$JAUTO)
has "principal remaining" "$P" "Principal remaining"
has "rate" "$P" "6.49% APR"
has "monthly payment" "$P" "Monthly payment"
has "next payment + due date" "$P" "Next payment"
has "payments made / to go" "$P" "to go"
has "payoff progress bar" "$P" 'aria-label="Loan paid off"'
has "Make a payment" "$P" "/loans/$JAUTO/pay"
has "Schedule tab" "$P" 'data-bs-target="#schedule"'
has "Payments & autopay tab" "$P" 'data-bs-target="#payments"'
has "schedule ends at \$0.00" "$P" "Last payment"
has "rows show what's owed" "$P" 'tx-bal">Owed'
hasnt "no Card tab on a loan" "$P" 'data-bs-target="#card"'
hasnt "no Direct Deposit tab on a loan" "$P" 'data-bs-target="#direct-deposit"'
check "paid rows + upcoming rows = the 60-month term" "$(printf '%s' "$P" | grep -cE '<tr class="(is-paid|is-next|)">')" 60
MP=$(curl -s -b $M $B/accounts/$MLOAN)
has "Maya: overdue banner" "$MP" "payment is overdue"
has "Maya: the fee is shown" "$MP" "in late fees"
has "paid-off loan: banner + letter" "$(curl -s -b $J $B/accounts/$JOLD)" "/loans/$JOLD/letter"
LT=$(curl -s -b $J $B/loans/$JOLD/letter)
has "paid-in-full letter" "$LT" "PAID IN FULL"
has "letter says \$0.00" "$LT" '\$0.00'
check "no letter for an active loan (back to the loan)" "$(code -b $J $B/loans/$JAUTO/letter)" 302
check "the old statement URL sends a loan to its schedule" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $J $B/accounts/$JAUTO/statements/2026/10)" "$B/accounts/$JAUTO#schedule"
AP=$(curl -s -b $J $B/accounts)
has "accounts list: loan card with Make a payment" "$AP" "/loans/$JAUTO/pay"
hasnt "accounts list: no deposit link for a loan" "$AP" "accountId=$JAUTO"

echo "== Dashboard: loan + card in Your accounts, net worth"
DB=$(curl -s -b $J $B/dashboard)
has "dashboard lists the auto loan" "$DB" "Auto Loan"
has "dashboard lists the card" "$DB" "Alto Peak"
has "dashboard says net worth" "$DB" "Net worth"
NW=$(sql "SELECT printf('%.2f', SUM(CASE WHEN CAST(Balance AS REAL) > 0 THEN CAST(Balance AS REAL) ELSE 0 END) - SUM(CASE WHEN AccountType IN ('CreditCard','Loan') AND CAST(Balance AS REAL) < 0 THEN -CAST(Balance AS REAL) ELSE 0 END)) FROM Accounts WHERE UserId = '$JID'")
has "net worth = assets - cards and loans ($(fmt $NW))" "$DB" "$(fmt $NW | tr -d '-')"
has "the breakdown says what you owe" "$DB" "you owe on cards and loans"

echo "== Ownership"
check "another customer's loan page -> 404" "$(code -b $J $B/accounts/$MLOAN)" 404
check "another customer's pay page -> 404" "$(code -b $J $B/loans/$MLOAN/pay)" 404
check "another customer's letter -> 404" "$(code -b $M $B/loans/$JOLD/letter)" 404
MB=$(bal $MLOAN)
check "pay another customer's loan -> 404" "$(post $J /accounts/$JAUTO /loans/$MLOAN/pay -d Kind=Scheduled -d FromAccountId=$JCHK | cut -c1-3)" 404
check "autopay on another customer's loan -> 404" "$(post $J /accounts/$JAUTO /loans/$MLOAN/autopay -d enabled=false | cut -c1-3)" 404
check "Maya's loan untouched" "$(bal $MLOAN)" "$MB"
SAPP=$(sql "SELECT Id FROM AccountApplications WHERE UserId = '$SID' AND AccountType = 'Loan'")
check "another customer's offer -> 404" "$(code -b $J $B/loans/offer/$SAPP)" 404
check "accept another customer's offer -> 404" "$(post $J /accounts/$JAUTO /loans/offer/$SAPP/accept -d toAccountId=$JCHK -d agree=true | cut -c1-3)" 404
check "API: another customer's loan -> 404" "$(code -b $J $B/api/loans/$MLOAN)" 404
check "API: another customer's schedule -> 404" "$(code -b $J $B/api/loans/$MLOAN/schedule)" 404
check "anonymous -> login" "$(code $B/loans/$JAUTO/pay)" 302
check "API anonymous -> 401" "$(code $B/api/loans)" 401
check "admin can't use customer loan pages" "$(code -b $A $B/loans/$JAUTO/pay)" 404
check "no antiforgery token -> 400" "$(code -b $J -X POST -d "Kind=Scheduled&FromAccountId=$JCHK" $B/loans/$JAUTO/pay)" 400

echo "== A loan isn't a deposit account"
JB=$(bal $JAUTO)
post $J /Transactions/Transfer /Transactions/Transfer -d FromAccountId=$JCHK -d ToAccountId=$JAUTO -d Amount=5 >/dev/null
has "transfer TO the loan refused (use Make a payment)" "$(cat /tmp/post.html)" "Use Make a payment"
post $J /Transactions/Deposit /Transactions/Deposit -d AccountId=$JAUTO -d Amount=5 >/dev/null
has "deposit into the loan refused" "$(cat /tmp/post.html)" "is a loan, so it can"
check "loan balance unchanged" "$(bal $JAUTO)" "$JB"
hasnt "loan not offered in the transfer form" "$(curl -s -b $J $B/Transactions/Transfer)" "value=\"$JAUTO\""
check "API transfer from the loan -> 400" "$(code -b $J -H 'Content-Type: application/json' -d "{\"fromAccountId\":$JAUTO,\"toAccountId\":$JCHK,\"amount\":5}" $B/api/transfers)" 400

echo "== Applying (simulated decision, real application flow)"
apply_user() {   # apply_user <jar> <email> → registers and verifies identity; sets IDTOK
  local T=$(tok $1 /Auth/Register)
  curl -s -b $1 -c $1 -o /dev/null --data-urlencode "FirstName=Riley" --data-urlencode "LastName=Stone" --data-urlencode "Email=$2" \
    --data-urlencode "Password=Passw0rd!" --data-urlencode "ConfirmPassword=Passw0rd!" --data-urlencode "__RequestVerificationToken=$T" $B/Auth/Register
  T=$(tok $1 /Accounts/Apply)
  IDTOK=$(curl -s -b $1 -c $1 -X POST -H "RequestVerificationToken: $T" $B/Accounts/VerifyIdentity | sed 's/.*"token":"\([^"]*\)".*/\1/')
}
apply() {   # apply <jar> <income> [extra -d args] → "status redirect" (verifies identity for this user first)
  local J=$1 INC=$2; shift 2
  local T=$(tok $J /Accounts/Apply)
  IDTOK=$(curl -s -b $J -c $J -X POST -H "RequestVerificationToken: $T" $B/Accounts/VerifyIdentity | sed 's/.*"token":"\([^"]*\)".*/\1/')
  post $J /Accounts/Apply /Accounts/Apply -d AccountType=Loan -d FirstName=Riley -d LastName=Stone -d DobMonth=5 -d DobDay=9 -d DobYear=1990 \
    -d Citizenship=US --data-urlencode "Phone=(814) 555-0111" --data-urlencode "Street=12 Ridge Rd" -d City=Altoona -d State=PA -d Zip=16601 \
    -d LivedTwoYears=true -d EmploymentStatus=Employed --data-urlencode "Employer=Ridge Co" -d Occupation=Planner --data-urlencode "IncomeRange=$INC" \
    -d SourceOfFunds=Employment --data-urlencode "IdentityToken=$IDTOK" --data-urlencode "QuizStreet=12 Ridge Rd" -d QuizZip=16601 \
    -d AgreePrivacy=true -d AgreeESign=true --data-urlencode "ESignName=Riley Stone" "$@"
}
HIGH='$200,000 or more'; LOW='Under $25,000'   # (ASCII only: Windows mangles non-ASCII curl arguments)
newuser() { local jar=$(mktemp) e="loan$RANDOM$RANDOM@test.com"; apply_user $jar $e >/dev/null; echo "$jar $(uid $(echo $e | tr a-z A-Z))"; }   # prints "<jar> <user id>"
read U UID_ <<< "$(newuser)"; UCHK=$(chk $UID_)
APPLY=$(curl -s -b $U "$B/Accounts/Apply?type=Loan&kind=Auto")
has "apply page: Alto Loan product" "$APPLY" 'value="Loan"'
has "apply page: personal or auto" "$APPLY" 'name="LoanKind"'
has "apply page: ?kind=Auto preselects auto" "$APPLY" 'value="Auto" class="visually-hidden" checked'
has "apply page: live estimate box with the rates" "$APPLY" "data-loan-estimate"
has "apply page: demo note" "$APPLY" "Demo decision, no credit check"
NA=$(sql "SELECT COUNT(*) FROM AccountApplications WHERE UserId = '$UID_'")
apply $U "$HIGH" -d LoanKind=Auto -d LoanAmount=4000 -d LoanTermMonths=36 --data-urlencode "LoanPurpose=Used vehicle" -d VehicleYear=2021 -d VehicleMake=Subaru -d VehicleModel=Outback -d DisburseToAccountId=$UCHK >/dev/null
has "auto under \$5,000 refused" "$(cat /tmp/post.html)" "Enter an amount from \$5,000 to \$50,000"
apply $U "$HIGH" -d LoanKind=Auto -d LoanAmount=9000 -d LoanTermMonths=12 --data-urlencode "LoanPurpose=Used vehicle" -d VehicleYear=2021 -d VehicleMake=Subaru -d VehicleModel=Outback -d DisburseToAccountId=$UCHK >/dev/null
has "auto 12-month term refused" "$(cat /tmp/post.html)" "Choose a term from 24 to 72 months"
apply $U "$HIGH" -d LoanKind=Auto -d LoanAmount=9000 -d LoanTermMonths=36 --data-urlencode "LoanPurpose=Used vehicle" -d DisburseToAccountId=$UCHK >/dev/null
has "auto without the vehicle refused" "$(cat /tmp/post.html)" "Enter the make"
apply $U "$HIGH" -d LoanKind=Personal -d LoanAmount=5000.50 -d LoanTermMonths=36 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$UCHK >/dev/null
has "cents refused (whole dollars)" "$(cat /tmp/post.html)" "whole dollars"
apply $U "$HIGH" -d LoanKind=Personal -d LoanAmount=5000 -d LoanTermMonths=36 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$JCHK >/dev/null
has "money to someone else's checking refused" "$(cat /tmp/post.html)" "Choose the Alto checking account"
apply $U "$HIGH" -d LoanKind=Personal -d LoanAmount=5000 -d LoanTermMonths=36 --data-urlencode "LoanPurpose=Buy a boat" -d DisburseToAccountId=$UCHK >/dev/null
has "a purpose not in the list refused" "$(cat /tmp/post.html)" "Choose what the loan is for"
check "nothing saved by the refused applications" "$(sql "SELECT COUNT(*) FROM AccountApplications WHERE UserId = '$UID_'")" "$NA"

# OFFER: high income, a modest loan.
R=$(apply $U "$HIGH" -d LoanKind=Personal -d LoanAmount=8000 -d LoanTermMonths=36 --data-urlencode "LoanPurpose=Debt consolidation" -d DisburseToAccountId=$UCHK)
APP=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$UID_'")
check "decision: an offer (status Offered)" "$(sql "SELECT Status || ':' || LoanRateTier || ':' || LoanAprPercent FROM AccountApplications WHERE Id = $APP")" "Offered:A:8.99"
check "submit -> PRG to the decision" "$R" "302 $B/accounts/decision/$APP"
check "the decision page sends a loan to its offer" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $U $B/Accounts/Decision/$APP)" "$B/loans/offer/$APP"
OF=$(curl -s -b $U $B/loans/offer/$APP)
EXP=$(node "$HERE/amortize.js" 8000 8.99 36)
has "offer: Truth in Lending box" "$OF" "Truth in Lending disclosure"
has "offer: APR" "$OF" "8.99%"
has "offer: finance charge = the schedule's total interest" "$OF" "$(fmt $(echo $EXP | cut -d' ' -f3))"
has "offer: total of payments" "$OF" "$(fmt $(echo $EXP | cut -d' ' -f4))"
has "offer: monthly payment" "$OF" "$(fmt $(echo $EXP | cut -d' ' -f1))"
has "offer: demo note" "$OF" "Demo decision, no credit check"
has "offer: choose where the money goes" "$OF" 'name="toAccountId"'
post $U /loans/offer/$APP /loans/offer/$APP/accept -d toAccountId=$UCHK >/dev/null
has "accept without agreeing refused" "$(cat /tmp/post.html)" "Truth in Lending disclosure and the Loan Agreement"
check "nothing lent yet" "$(sql "SELECT COUNT(*) FROM Loans WHERE UserId = '$UID_'")" 0
apply $U "$HIGH" -d LoanKind=Personal -d LoanAmount=2000 -d LoanTermMonths=12 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$UCHK >/dev/null
has "a second loan application while an offer is open is refused" "$(cat /tmp/post.html)" "loan application in progress"
NAUD=$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Loan.Disburse'")
R=$(post $U /loans/offer/$APP /loans/offer/$APP/accept -d toAccountId=$UCHK -d agree=true)
ULOAN=$(sql "SELECT AccountId FROM Loans WHERE UserId = '$UID_'")
check "accept -> PRG to the new loan" "$R" "302 $B/accounts/$ULOAN"
has "says the money arrived" "$(flash $U $B/accounts/$ULOAN)" "Your loan is funded"
check "loan account owes the full amount" "$(bal $ULOAN)" "-8000.00"
check "checking got the money" "$(bal $UCHK)" "8000.00"
check "both sides recorded and linked" "$(sql "SELECT a.Type || '>' || b.Type FROM Transactions a JOIN Transactions b ON b.Id = a.LinkedTransactionId WHERE a.AccountId = $ULOAN AND a.Type = 'LoanDisbursement' AND b.AccountId = $UCHK")" "LoanDisbursement>LoanProceeds"
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Loan.Disburse'")" "$((NAUD + 1))"
check "the application is Approved and points at the loan" "$(sql "SELECT Status || ':' || AccountId FROM AccountApplications WHERE Id = $APP")" "Approved:$ULOAN"
check "first payment one month out, monthly payment from LoanMath" "$(sql "SELECT FirstDueDate || ':' || printf('%.2f', MonthlyPayment) FROM Loans WHERE AccountId = $ULOAN")" "$(sql "SELECT CASE WHEN CAST(strftime('%d', date('$TODAY', '+1 month')) AS INT) > 28 THEN strftime('%Y-%m-28', date('$TODAY', '+1 month')) ELSE date('$TODAY', '+1 month') END"):$(echo $EXP | cut -d' ' -f1)"
inv "new loan" $ULOAN
post $U /loans/offer/$APP /loans/offer/$APP/accept -d toAccountId=$UCHK -d agree=true >/dev/null
check "accepting again doesn't lend twice (back to the loan)" "$(sql "SELECT COUNT(*) FROM Loans WHERE UserId = '$UID_'")" 1
check "checking credited once" "$(bal $UCHK)" "8000.00"

# DENIED: low income, a payment far over a quarter of it.
read U2 U2ID <<< "$(newuser)"; U2CHK=$(chk $U2ID)
apply $U2 "$LOW" -d LoanKind=Personal -d LoanAmount=10000 -d LoanTermMonths=12 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$U2CHK >/dev/null
A2=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$U2ID'")
check "decision: denied" "$(sql "SELECT Status FROM AccountApplications WHERE Id = $A2")" "Denied"
ST=$(curl -s -b $U2 $B/Accounts/Decision/$A2)
has "status page: the reason" "$ST" "more than a quarter of the monthly income"
has "status page: demo note" "$ST" "Demo decision, no credit check"
check "no loan for a denial" "$(sql "SELECT COUNT(*) FROM Loans WHERE UserId = '$U2ID'")" 0
check "a denied application can't be accepted (shows its status)" "$(post $U2 /Accounts/Apply /loans/offer/$A2/accept -d toAccountId=$U2CHK -d agree=true | cut -c1-3)" 200
check "still no loan" "$(sql "SELECT COUNT(*) FROM Loans WHERE UserId = '$U2ID'")" 0

# PENDING (large) → admin approves as offered → the customer accepts.
read U3 U3ID <<< "$(newuser)"; U3CHK=$(chk $U3ID)
apply $U3 "$HIGH" -d LoanKind=Personal -d LoanAmount=22000 -d LoanTermMonths=60 --data-urlencode "LoanPurpose=Home improvement" -d DisburseToAccountId=$U3CHK >/dev/null
A3=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$U3ID'")
check "decision: large loan → pending, with the terms staff will see" "$(sql "SELECT Status || ':' || (LoanAprPercent IS NOT NULL) FROM AccountApplications WHERE Id = $A3")" "Pending:1"
has "customer sees it's in review" "$(curl -s -b $U3 $B/Accounts/Decision/$A3)" "reviewing your application"
AD=$(curl -s -b $A $B/admin/applications/details/$A3)
has "admin: loan terms on the application" "$AD" "Proposed terms"
has "admin: approve as offered" "$AD" "Approve as offered"
has "admin: counter-offer button" "$AD" "Counter-offer"
has "admin: demo note" "$AD" "Demo decision, no credit check"
apost /admin/applications/details/$A3 /admin/applications/approve/$A3 --data-urlencode "reason=Income verified for a large loan" >/dev/null
check "admin approve → an offer (nothing lent yet)" "$(sql "SELECT Status || ':' || IsCounterOffer FROM AccountApplications WHERE Id = $A3"):$(sql "SELECT COUNT(*) FROM Loans WHERE UserId = '$U3ID'")" "Offered:0:0"
check "the customer is told" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$U3ID' AND Type = 'LoanUpdate' AND Link = '/loans/offer/$A3'")" 1
post $U3 /loans/offer/$A3 /loans/offer/$A3/accept -d toAccountId=$U3CHK -d agree=true >/dev/null
check "accepted after staff approval" "$(sql "SELECT printf('%.2f', Principal) FROM Loans WHERE UserId = '$U3ID'")" "22000.00"

# COUNTER-OFFER: pending → staff counter (amount and rate) → the customer sees and accepts the new terms.
read U4 U4ID <<< "$(newuser)"; U4CHK=$(chk $U4ID)
apply $U4 "$HIGH" -d LoanKind=Auto -d LoanAmount=30000 -d LoanTermMonths=60 --data-urlencode "LoanPurpose=New vehicle" -d VehicleYear=2026 -d VehicleMake=Honda -d VehicleModel=CR-V -d DisburseToAccountId=$U4CHK >/dev/null
A4=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$U4ID'")
apost /admin/applications/details/$A4 /admin/applications/counter/$A4 -d amount=30000 -d apr=$(sql "SELECT LoanAprPercent FROM AccountApplications WHERE Id = $A4") --data-urlencode "reason=Same terms" >/dev/null
has "a counter with the same terms is refused" "$(flash $A $B/admin/applications/details/$A4)" "same as the current terms"
apost /admin/applications/details/$A4 /admin/applications/counter/$A4 -d amount=60000 --data-urlencode "reason=Too much" >/dev/null
has "a counter outside the auto range is refused" "$(flash $A $B/admin/applications/details/$A4)" "whole dollars from \$5,000 to \$50,000"
apost /admin/applications/details/$A4 /admin/applications/counter/$A4 -d amount=24000 -d apr=7.25 --data-urlencode "reason=" >/dev/null
check "a counter needs a reason" "$(sql "SELECT Status FROM AccountApplications WHERE Id = $A4")" "Pending"
apost /admin/applications/details/$A4 /admin/applications/counter/$A4 -d amount=24000 -d apr=7.25 --data-urlencode "reason=Lower amount fits the income" >/dev/null
check "counter-offer: new amount, rate and payment" "$(sql "SELECT Status || ':' || IsCounterOffer || ':' || printf('%.2f', LoanAmount) || ':' || LoanAprPercent || ':' || printf('%.2f', LoanMonthlyPayment) || ':' || printf('%.2f', RequestedLoanAmount) FROM AccountApplications WHERE Id = $A4")" "Offered:1:24000.00:7.25:$(node "$HERE/amortize.js" 24000 7.25 60 | cut -d' ' -f1):30000.00"
check "counter-offer audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Application.CounterOffer' AND TargetId = '$A4'")" 1
CO=$(curl -s -b $U4 $B/loans/offer/$A4)
has "customer sees what they asked for" "$CO" "You asked for"
has "and the new amount" "$CO" "24,000.00"
has "and the new rate" "$CO" "7.25%"
post $U4 /loans/offer/$A4 /loans/offer/$A4/accept -d toAccountId=$U4CHK -d agree=true >/dev/null
check "counter-offer accepted: the loan has the countered terms" "$(sql "SELECT printf('%.2f', Principal) || ':' || AprPercent || ':' || printf('%.2f', MonthlyPayment) FROM Loans WHERE UserId = '$U4ID'")" "24000.00:7.25:$(node "$HERE/amortize.js" 24000 7.25 60 | cut -d' ' -f1)"
check "checking got the countered amount" "$(bal $U4CHK)" "24000.00"

# DECLINE + EXPIRY
read U5 U5ID <<< "$(newuser)"; U5CHK=$(chk $U5ID)
apply $U5 "$HIGH" -d LoanKind=Personal -d LoanAmount=3000 -d LoanTermMonths=24 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$U5CHK >/dev/null
A5=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$U5ID'")
post $U5 /loans/offer/$A5 /loans/offer/$A5/decline >/dev/null
check "declined" "$(sql "SELECT Status FROM AccountApplications WHERE Id = $A5")" "OfferDeclined"
post $U5 /loans/offer/$A5 /loans/offer/$A5/accept -d toAccountId=$U5CHK -d agree=true >/dev/null
check "a declined offer can't be accepted" "$(sql "SELECT COUNT(*) FROM Loans WHERE UserId = '$U5ID'")" 0
has "status page: you declined" "$(curl -s -b $U5 $B/loans/offer/$A5)" "You declined this loan offer"
apply $U5 "$HIGH" -d LoanKind=Personal -d LoanAmount=3000 -d LoanTermMonths=24 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$U5CHK >/dev/null
A6=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$U5ID'")
check "after declining, a new application is allowed" "$(sql "SELECT Status FROM AccountApplications WHERE Id = $A6")" "Offered"
sql "UPDATE AccountApplications SET OfferedAt = datetime('now', '-32 days') WHERE Id = $A6" >/dev/null
has "an expired offer says so" "$(curl -s -b $U5 $B/loans/offer/$A6)" "This offer expired"
post $U5 /loans/offer/$A6 /loans/offer/$A6/accept -d toAccountId=$U5CHK -d agree=true >/dev/null
has "an expired offer can't be accepted" "$(cat /tmp/post.html)" "expired"
check "no loan" "$(sql "SELECT COUNT(*) FROM Loans WHERE UserId = '$U5ID'")" 0
has "an expired offer can be removed" "$(curl -s -b $U5 $B/loans/offer/$A6)" "Remove this offer"
hasnt "an expired offer doesn't block a new application" "$(curl -s -b $U5 "$B/Accounts/Apply?type=Loan")" "loan application in progress"
apply $U5 "$HIGH" -d LoanKind=Personal -d LoanAmount=2500 -d LoanTermMonths=12 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$U5CHK >/dev/null
check "after an offer expires, the customer can apply again" "$(sql "SELECT Status FROM AccountApplications WHERE UserId = '$U5ID' ORDER BY Id DESC LIMIT 1")" "Offered"

echo "== Payments: scheduled, extra principal, payoff"
# Riley (U) has the new $8,000 loan and $8,000 in checking.
UL=$ULOAN
I1=$(node -e "const p=8000,r=8.99/1200;console.log((Math.round(p*r*100+1e-9)/100).toFixed(2))")
MPAY=$(lv $UL "printf('%.2f', MonthlyPayment)")
pay $U $UL -d Kind=Scheduled -d FromAccountId=$UCHK >/dev/null
has "no monthly payment on the day the loan is funded (it would cost a full month of interest)" "$(cat /tmp/post.html)" "Your first payment is due"
check "nothing paid" "$(lv $UL PaymentsMade):$(bal $UCHK)" "0:8000.00"
# Move time: the loan was funded 12 days ago, so its first period has started.
sql "UPDATE Loans SET DisbursedAt = datetime(DisbursedAt, '-12 days'), NextDueDate = '$(d '+18 days')' WHERE AccountId = $UL" >/dev/null
DUE1=$(lv $UL NextDueDate)
PP=$(curl -s -b $U $B/loans/$UL/pay)
has "pay page: the form carries the due date it pays" "$PP" "name=\"ForDueDate\" value=\"$DUE1\""
has "pay page: monthly payment option" "$PP" "Monthly payment"
has "pay page: shows the interest/principal split" "$PP" "\$$I1 interest"
has "pay page: extra toward principal" "$PP" "Extra toward principal"
has "pay page: payoff with interest to today" "$PP" "interest to today"
R=$(pay $U $UL -d Kind=Scheduled -d FromAccountId=$UCHK)
check "scheduled payment -> PRG to the loan" "$R" "302 $B/accounts/$UL"
check "paid the monthly payment from checking" "$(bal $UCHK)" "$(money "8000 - $MPAY")"
check "interest first, the rest to principal" "$(lv $UL "printf('%.2f', PrincipalRemaining)")" "$(money "8000 - ($MPAY - $I1)")"
check "one installment made, next due a month later" "$(lv $UL "PaymentsMade || ':' || NextDueDate")" "1:$(date -d "$DUE1 +1 month" +%F)"
check "rows: interest charge + payment on the loan, transfer out of checking" "$(sql "SELECT GROUP_CONCAT(Type) FROM (SELECT Type FROM Transactions WHERE AccountId = $UL AND Type <> 'LoanDisbursement' ORDER BY Id)"):$(sql "SELECT Type FROM Transactions WHERE AccountId = $UCHK ORDER BY Id DESC LIMIT 1")" "InterestCharge,LoanPayment:TransferOut"
check "payment record: the split" "$(sql "SELECT Kind || ':' || InstallmentNumber || ':' || printf('%.2f', Interest) FROM LoanPayments WHERE LoanId = (SELECT Id FROM Loans WHERE AccountId = $UL)")" "Scheduled:1:$I1"
inv "after a scheduled payment" $UL
pay $U $UL -d Kind=Scheduled -d FromAccountId=$UCHK >/dev/null
has "the same month can't be paid twice" "$(cat /tmp/post.html)" "payment is made"
pay $U $UL -d Kind=Scheduled -d ForDueDate=$DUE1 -d FromAccountId=$UCHK >/dev/null
has "a resubmitted form for a paid due date is refused" "$(cat /tmp/post.html)" "was already made, so nothing was paid again"
check "still one installment" "$(lv $UL PaymentsMade)" 1
LEFT_BEFORE=$(curl -s -b $U $B/api/loans/$UL | grep -oE '"paymentsRemaining":[0-9]+' | cut -d: -f2)
PR=$(lv $UL "printf('%.2f', PrincipalRemaining)")
R=$(pay $U $UL -d Kind=ExtraPrincipal -d ExtraAmount=1500 -d FromAccountId=$UCHK)
check "extra principal -> PRG" "$R" "302 $B/accounts/$UL"
check "extra goes entirely to principal" "$(lv $UL "printf('%.2f', PrincipalRemaining)")" "$(money "$PR - 1500")"
LEFT_AFTER=$(curl -s -b $U $B/api/loans/$UL | grep -oE '"paymentsRemaining":[0-9]+' | cut -d: -f2)
range() { awk -v v="$2" -v lo="$3" -v hi="$4" 'BEGIN { exit !(v >= lo && v <= hi) }' && ok "$1 ($2)" || bad "$1: [$2] not in [$3, $4]"; }
range "extra principal shortens the schedule (was $LEFT_BEFORE)" "$LEFT_AFTER" 1 $((LEFT_BEFORE - 1))
check "the monthly payment stays the same" "$(lv $UL "printf('%.2f', MonthlyPayment)")" "$MPAY"
check "extra doesn't count as an installment" "$(lv $UL PaymentsMade)" 1
pay $U $UL -d Kind=ExtraPrincipal -d ExtraAmount=99999 -d FromAccountId=$UCHK >/dev/null
has "extra that would pay it all off -> use Pay off" "$(cat /tmp/post.html)" "Pay off the loan"
inv "after extra principal" $UL
# Payoff quote: the period started 10 days ago → 10 days of interest.
sql "UPDATE Loans SET NextDueDate = '$(d '-10 days +1 month')' WHERE AccountId = $UL" >/dev/null
PR=$(lv $UL "printf('%.2f', PrincipalRemaining)")
ACC=$(node -e "const p=Math.round($PR*100), bp=899; const num=BigInt(p)*BigInt(bp)*10n, den=3650000n; const c=(2n*num+den)/(2n*den); console.log((Number(c)/100).toFixed(2))")
QUOTE=$(curl -s -b $U $B/api/loans/$UL | grep -oE '"payoffAmountToday":[0-9.]+' | cut -d: -f2)
check "payoff quote = principal + 10 days of interest (\$$ACC)" "$(money $QUOTE)" "$(money "$PR + $ACC")"
has "pay page shows the payoff and the days" "$(curl -s -b $U $B/loans/$UL/pay)" "10 days since"
pay $U $UL -d Kind=Payoff -d FromAccountId=$UCHK >/dev/null
has "a payoff without enough money is refused" "$(cat /tmp/post.html)" "Insufficient funds"
check "nothing changed" "$(lv $UL Status)" "Active"
apost /admin/accounts/details/$UCHK /admin/accounts/adjust/$UCHK -d direction=credit -d amount=1000 --data-urlencode "reason=Test: payday before the payoff" >/dev/null
has "(\$0.00 used today after \$$(money "$MPAY + 1500") of loan payments)" "$(curl -s -b $U $B/accounts/$UCHK)" '\$0.00</span> used today'
CB=$(bal $UCHK)
R=$(pay $U $UL -d Kind=Payoff -d FromAccountId=$UCHK)
check "payoff -> PRG" "$R" "302 $B/accounts/$UL"
has "says paid in full" "$(flash $U $B/accounts/$UL)" "paid in full"
check "the loan is at exactly \$0.00" "$(bal $UL)" "0.00"
check "checking paid the quote (more than the \$5,000 daily transfer limit is fine for your own loan)" "$(bal $UCHK)" "$(money "$CB - $QUOTE")"
check "loan PaidOff, account Closed" "$(sql "SELECT l.Status || ':' || a.Status FROM Loans l JOIN Accounts a ON a.Id = l.AccountId WHERE l.AccountId = $UL")" "PaidOff:Closed"
check "the payoff's interest row is the accrued interest" "$(sql "SELECT printf('%.2f', Amount) FROM Transactions WHERE AccountId = $UL AND Type = 'InterestCharge' ORDER BY Id DESC LIMIT 1")" "$ACC"
check "paid-in-full letter" "$(code -b $U $B/loans/$UL/letter)" 200
pay $U $UL -d Kind=Scheduled -d FromAccountId=$UCHK >/dev/null
check "nothing more can be paid" "$(bal $UL)" "0.00"
check "the pay page sends you back" "$(code -b $U $B/loans/$UL/pay)" 302
check "API: paid off" "$(json_field "$(curl -s -b $U $B/api/loans/$UL)" status)" "PaidOff"
check "a payoff alert links the letter" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$UID_' AND Link = '/loans/$UL/letter'")" 1

echo "== The final scheduled payment lands on \$0.00"
# A fresh small loan paid month by month to the end (each time "moving" to the next period).
read U6 U6ID <<< "$(newuser)"; U6CHK=$(chk $U6ID)
apply $U6 "$HIGH" -d LoanKind=Personal -d LoanAmount=1000 -d LoanTermMonths=12 --data-urlencode "LoanPurpose=Moving" -d DisburseToAccountId=$U6CHK >/dev/null
A7=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$U6ID'")
post $U6 /loans/offer/$A7 /loans/offer/$A7/accept -d toAccountId=$U6CHK -d agree=true >/dev/null
L6=$(sql "SELECT AccountId FROM Loans WHERE UserId = '$U6ID'")
apost /admin/accounts/details/$U6CHK /admin/accounts/adjust/$U6CHK -d direction=credit -d amount=200 --data-urlencode "reason=Test: interest money" >/dev/null
EXP6=$(node "$HERE/amortize.js" 1000 8.99 12)
for i in $(seq 1 12); do
  sql "UPDATE Loans SET NextDueDate = '$(d '+20 days')' WHERE AccountId = $L6 AND Status = 'Active'" >/dev/null
  pay $U6 $L6 -d Kind=Scheduled -d FromAccountId=$U6CHK >/dev/null
done
check "12 scheduled payments, no more" "$(lv $L6 PaymentsMade)" 12
check "the loan ends at exactly \$0.00 and is paid off" "$(bal $L6):$(lv $L6 Status)" "0.00:PaidOff"
check "the last payment was the adjusted final payment" "$(sql "SELECT printf('%.2f', Amount) FROM LoanPayments WHERE LoanId = (SELECT Id FROM Loans WHERE AccountId = $L6) ORDER BY InstallmentNumber DESC LIMIT 1")" "$(echo $EXP6 | cut -d' ' -f2)"
check "total interest = the independent schedule's" "$(sql "SELECT printf('%.2f', SUM(CAST(Interest AS REAL))) FROM LoanPayments WHERE LoanId = (SELECT Id FROM Loans WHERE AccountId = $L6)")" "$(echo $EXP6 | cut -d' ' -f3)"

echo "== Late fee + overdue (grace days) + admin waive"
# Counter-offer customer (U4) has the \$24,000 loan and the money in checking.
L4=$(sql "SELECT AccountId FROM Loans WHERE UserId = '$U4ID'")
sql "UPDATE Loans SET NextDueDate = '$(d '-5 days')' WHERE AccountId = $L4" >/dev/null
sleep 6
check "inside the grace days: no fee" "$(sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $L4 AND Type = 'LateFee'")" 0
has "loan page: was due, pay by the grace date" "$(curl -s -b $U4 $B/accounts/$L4)" "to avoid a"
sql "UPDATE Loans SET NextDueDate = '$(d '-11 days')' WHERE AccountId = $L4" >/dev/null
waitfor "more than 10 days late: the late fee is charged" "SELECT COUNT(*) || ':' || printf('%.2f', (SELECT FeesOwed FROM Loans WHERE AccountId = $L4)) FROM Transactions WHERE AccountId = $L4 AND Type = 'LateFee'" "1:35.00"
check "LateFeeForDueDate remembers it" "$(lv $L4 LateFeeForDueDate)" "$(d '-11 days')"
check "customer alerted" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$U4ID' AND Type = 'LoanLateFee'")" 1
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Loan.LateFee' AND TargetId = '$L4'")" 1
sleep 6
check "charged once per missed payment (the next run adds nothing)" "$(sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $L4 AND Type = 'LateFee'")" 1
inv "after the fee" $L4
check "API: overdue" "$(json_field "$(curl -s -b $U4 $B/api/loans/$L4)" overdue)" "true"
has "loan page: overdue banner" "$(curl -s -b $U4 $B/accounts/$L4)" "payment is overdue"
AO=$(curl -s -b $A "$B/admin/loans?status=overdue")
has "admin: the Overdue filter lists it" "$AO" "/admin/loans/details/$L4"
has "admin: Maya's seeded loan is overdue too" "$AO" "/admin/loans/details/$MLOAN"
hasnt "admin: Jordan's on-time loan isn't overdue" "$AO" "/admin/loans/details/$JAUTO\""
SCHED=$(lv $L4 "printf('%.2f', MonthlyPayment)")
pay $U4 $L4 -d Kind=Scheduled -d FromAccountId=$U4CHK >/dev/null
check "the scheduled payment includes the fee (fees first)" "$(sql "SELECT printf('%.2f', Amount) || ':' || printf('%.2f', Fees) FROM LoanPayments WHERE LoanId = (SELECT Id FROM Loans WHERE AccountId = $L4) ORDER BY Id DESC LIMIT 1")" "$(money "$SCHED + 35"):35.00"
check "fees cleared" "$(lv $L4 "printf('%.2f', FeesOwed)")" "0.00"
inv "after paying with the fee" $L4
# Admin waive (Maya's seeded fee, still owed).
MFEE=$(sql "SELECT Id FROM Transactions WHERE AccountId = $MLOAN AND Type = 'LateFee' LIMIT 1")
MBAL=$(bal $MLOAN)
apost /admin/loans/details/$MLOAN /admin/loans/waivefee/$MLOAN -d feeTransactionId=$MFEE --data-urlencode "reason=" >/dev/null
check "waive needs a reason" "$(lv $MLOAN "printf('%.2f', FeesOwed)")" "35.00"
apost /admin/loans/details/$MLOAN /admin/loans/waivefee/$MLOAN -d feeTransactionId=$MFEE --data-urlencode "reason=First late payment, courtesy waiver" >/dev/null
check "waived: fees owed back to 0" "$(lv $MLOAN "printf('%.2f', FeesOwed)")" "0.00"
check "the balance goes down by the fee" "$(bal $MLOAN)" "$(money "$MBAL + 35")"
check "the fee is marked Reversed, linked to the waiver" "$(sql "SELECT f.Status || ':' || w.Type FROM Transactions f JOIN Transactions w ON w.Id = f.ReversedByTransactionId WHERE f.Id = $MFEE")" "Reversed:FeeWaiver"
check "audited with the reason" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Loan.WaiveFee' AND TargetId = '$MLOAN' AND Reason = 'First late payment, courtesy waiver'")" 1
apost /admin/loans/details/$MLOAN /admin/loans/waivefee/$MLOAN -d feeTransactionId=$MFEE --data-urlencode "reason=Trying again" >/dev/null
has "can't waive twice" "$(flash $A $B/admin/loans/details/$MLOAN)" "already waived"
inv "after the waiver" $MLOAN
check "still overdue (the payment itself isn't paid)" "$(json_field "$(curl -s -b $M $B/api/loans/$MLOAN)" overdue)" "true"
apost /admin/transactions/details/$MFEE /admin/transactions/reverse/$MFEE --data-urlencode "reason=Trying the other way" >/dev/null
has "loan rows can't be reversed on the Transactions page" "$(flash $A $B/admin/transactions/details/$MFEE)" "corrected on the Loans page"

echo "== Autopay"
# U3 has the \$22,000 loan and \$22,000 in checking.
L3=$(sql "SELECT AccountId FROM Loans WHERE UserId = '$U3ID'")
post $U3 /accounts/$L3 /loans/$L3/autopay -d enabled=true -d fromAccountId=$JCHK >/dev/null
has "autopay from someone else's account refused" "$(flash $U3 $B/accounts/$L3)" "Choose the checking or savings account"
R=$(post $U3 /accounts/$L3 /loans/$L3/autopay -d enabled=true -d fromAccountId=$U3CHK)
check "autopay on -> PRG to the payments tab" "$R" "302 $B/accounts/$L3#payments"
check "autopay saved" "$(lv $L3 "AutopayEnabled || ':' || AutopayFromAccountId")" "1:$U3CHK"
CB=$(bal $U3CHK); DUE=$(d '+0 days')
sql "UPDATE Loans SET NextDueDate = '$DUE' WHERE AccountId = $L3" >/dev/null
waitfor "on the due date the scheduler pays it" "SELECT PaymentsMade FROM Loans WHERE AccountId = $L3" 1
check "by autopay, for that due date" "$(sql "SELECT ByAutopay || ':' || DueDate FROM LoanPayments WHERE LoanId = (SELECT Id FROM Loans WHERE AccountId = $L3)"):$(lv $L3 AutopayAttemptedFor)" "1:$DUE:$DUE"
check "from checking" "$(bal $U3CHK)" "$(money "$CB - $(lv $L3 "printf('%.2f', MonthlyPayment)")")"
check "customer told" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$U3ID' AND Title = 'Loan autopay made'")" 1
inv "after autopay" $L3
# Failure: autopay from an account without the money.
apost /admin/accounts/details/$U3CHK /admin/accounts/adjust/$U3CHK -d direction=debit -d amount=$(bal $U3CHK) --data-urlencode "reason=Test: empty checking" >/dev/null
check "checking emptied" "$(bal $U3CHK)" "0.00"
NP=$(lv $L3 PaymentsMade); DUE=$(d '-1 days')
sql "UPDATE Loans SET NextDueDate = '$DUE', AutopayAttemptedFor = NULL WHERE AccountId = $L3" >/dev/null
waitfor "autopay tried for the due date" "SELECT AutopayAttemptedFor FROM Loans WHERE AccountId = $L3" "$DUE"
check "no payment was made" "$(lv $L3 PaymentsMade)" "$NP"
check "customer told it failed, with what to do" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$U3ID' AND Title = 'Loan autopay failed' AND Message LIKE '%to avoid a late fee%'")" 1
sleep 6
check "not retried every run (one failure alert)" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$U3ID' AND Title = 'Loan autopay failed'")" 1
inv "after failed autopay" $L3
apost /admin/accounts/details/$U3CHK /admin/accounts/adjust/$U3CHK -d direction=credit -d amount=2000 --data-urlencode "reason=Test: payday after a failed autopay" >/dev/null
sql "UPDATE Loans SET NextDueDate = '$(d '+0 days')' WHERE AccountId = $L3" >/dev/null   # (the missed payment, due today)
R=$(post $U3 /accounts/$L3 /loans/$L3/autopay -d enabled=true -d fromAccountId=$U3CHK)
has "saving autopay again says the past-due payment will be paid" "$(flash $U3 $B/accounts/$L3)" "past-due payment will be paid"
waitfor "and the scheduler retries it" "SELECT PaymentsMade FROM Loans WHERE AccountId = $L3" "$((NP + 1))"
inv "after the retried autopay" $L3
check "autopay and a manual payment can't both pay on the due date" "$(pay $U3 $L3 -d Kind=Scheduled -d FromAccountId=$U3CHK | cut -c1-3):$(lv $L3 PaymentsMade)" "200:$((NP + 1))"
apost /admin/accounts/details/$U3CHK /admin/accounts/adjust/$U3CHK -d direction=debit -d amount=$(bal $U3CHK) --data-urlencode "reason=Test: empty checking again" >/dev/null
sql "UPDATE Loans SET NextDueDate = '$(d '-1 days')' WHERE AccountId = $L3" >/dev/null
post $U3 /accounts/$L3 /loans/$L3/autopay -d enabled=false >/dev/null
check "autopay off" "$(lv $L3 "AutopayEnabled || ':' || IFNULL(AutopayFromAccountId, 'none')")" "0:none"
pay $U3 $L3 -d Kind=Scheduled -d FromAccountId=$U3CHK >/dev/null
has "a payment without the money is refused" "$(cat /tmp/post.html)" "Insufficient funds"

echo "== Admin: Loans page, adjustments, guards"
check "admin loans page" "$(code -b $A $B/admin/loans)" 200
has "admin nav has Loans" "$(curl -s -b $A $B/admin)" 'href="/admin/loans"'
AP=$(curl -s -b $A "$B/admin/loans?status=paidoff")
has "Paid off filter lists Jordan's old loan" "$AP" "/admin/loans/details/$JOLD"
hasnt "Paid off filter hides active loans" "$AP" "/admin/loans/details/$JAUTO\""
AD=$(curl -s -b $A $B/admin/loans/details/$JAUTO)
has "detail: schedule from here" "$AD" "Schedule from here"
has "detail: payment history" "$AD" "Payment history"
has "detail: post adjustment" "$AD" "Post adjustment"
has "detail: decision + demo note" "$AD" "Demo decision, no credit check"
check "generic account detail sends a loan to the Loans page" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $A $B/admin/accounts/details/$JAUTO)" "$B/admin/loans/details/$JAUTO"
PRJ=$(lv $JAUTO "printf('%.2f', PrincipalRemaining)"); JB=$(bal $JAUTO)
apost /admin/loans/details/$JAUTO /admin/loans/adjust/$JAUTO -d direction=credit -d amount=100 --data-urlencode "reason=" >/dev/null
check "adjustment needs a reason" "$(bal $JAUTO)" "$JB"
apost /admin/loans/details/$JAUTO /admin/loans/adjust/$JAUTO -d direction=credit -d amount=100 --data-urlencode "reason=Goodwill credit for a billing error" >/dev/null
check "credit lowers the principal" "$(lv $JAUTO "printf('%.2f', PrincipalRemaining)"):$(bal $JAUTO)" "$(money "$PRJ - 100"):$(money "$JB + 100")"
apost /admin/loans/details/$JAUTO /admin/loans/adjust/$JAUTO -d direction=debit -d amount=40.25 --data-urlencode "reason=Correcting part of the goodwill credit" >/dev/null
check "debit raises it" "$(lv $JAUTO "printf('%.2f', PrincipalRemaining)")" "$(money "$PRJ - 100 + 40.25")"
check "both audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Loan.Adjust' AND TargetId = '$JAUTO'")" 2
apost /admin/loans/details/$JAUTO /admin/loans/adjust/$JAUTO -d direction=credit -d amount=999999 --data-urlencode "reason=Way too much credit" >/dev/null
has "a credit over the principal is refused" "$(flash $A $B/admin/loans/details/$JAUTO)" "more than the principal still owed"
inv "after adjustments" $JAUTO
JB=$(bal $JAUTO)
apost /admin/loans/details/$JAUTO /admin/accounts/adjust/$JAUTO -d direction=credit -d amount=50 --data-urlencode "reason=Generic adjust" >/dev/null
has "no raw balance adjustment on a loan" "$(flash $A $B/admin/loans/details/$JAUTO)" "Use Loans in the menu"
apost /admin/loans/details/$JAUTO /admin/accounts/freeze/$JAUTO --data-urlencode "reason=Try to freeze" >/dev/null
has "no freeze on a loan" "$(flash $A $B/admin/loans/details/$JAUTO)" "Use Loans in the menu"
apost /admin/loans/details/$JAUTO /admin/accounts/close/$JAUTO --data-urlencode "reason=Try to close" >/dev/null
has "no generic close on a loan" "$(flash $A $B/admin/loans/details/$JAUTO)" "Use Loans in the menu"
check "loan untouched by the refused actions" "$(bal $JAUTO):$(sql "SELECT Status FROM Accounts WHERE Id = $JAUTO")" "$JB:Active"
apost /admin/loans/details/$JOLD /admin/loans/adjust/$JOLD -d direction=debit -d amount=10 --data-urlencode "reason=On a paid-off loan" >/dev/null
has "a paid-off loan can't be adjusted" "$(flash $A $B/admin/loans/details/$JOLD)" "paid off"
# Staff bring a loan to $0 owed (credit the principal, then waive the last fee): it's paid off, not left Active at $0.
L4P=$(lv $L4 "printf('%.2f', PrincipalRemaining)")
sql "UPDATE Loans SET NextDueDate = '$(d '-12 days')', LateFeeForDueDate = NULL WHERE AccountId = $L4" >/dev/null
waitfor "another missed payment: a second late fee" "SELECT COUNT(*) FROM Transactions WHERE AccountId = $L4 AND Type = 'LateFee' AND Status = 'Posted'" 2
apost /admin/loans/details/$L4 /admin/loans/adjust/$L4 -d direction=credit -d amount=$L4P --data-urlencode "reason=Settlement: principal forgiven" >/dev/null
check "principal forgiven, the fee still owed: still active" "$(lv $L4 "printf('%.2f', PrincipalRemaining) || ':' || printf('%.2f', FeesOwed) || ':' || Status")" "0.00:35.00:Active"
L4FEE=$(sql "SELECT Id FROM Transactions WHERE AccountId = $L4 AND Type = 'LateFee' AND Status = 'Posted' ORDER BY Id DESC LIMIT 1")
apost /admin/loans/details/$L4 /admin/loans/waivefee/$L4 -d feeTransactionId=$L4FEE --data-urlencode "reason=Settlement: fee waived too" >/dev/null
check "waiving the last fee pays the loan off and closes it" "$(sql "SELECT l.Status || ':' || a.Status || ':' || printf('%.2f', a.Balance) || ':' || l.AutopayEnabled FROM Loans l JOIN Accounts a ON a.Id = l.AccountId WHERE l.AccountId = $L4")" "PaidOff:Closed:0.00:0"
hasnt "and it isn't listed as overdue" "$(curl -s -b $A "$B/admin/loans?status=overdue")" "/admin/loans/details/$L4\""

echo "== Settings: rate tiers"
ST=$(curl -s -b $A $B/admin/settings)
has "settings: loan tiers" "$ST" "LoanRateTierA"
has "settings: grace days" "$ST" "LoanGraceDays"
TS=$(tok $A /admin/settings)
SET="SavingsApyPercent=4.10&CheckingApyPercent=0&DefaultDailyTransferLimit=5000&LargeTransactionThreshold=2500&AutoApproveApplications=true&MobileDepositPerCheckLimit=5000&MobileDepositDailyLimit=10000"
curl -s -b $A -c $A -o /dev/null -d "$SET&LoanRateTierA=12.5&LoanRateTierB=11.99&reason=Testing an out-of-order tier&__RequestVerificationToken=$TS" $B/admin/settings
check "tiers must go up from A to D" "$(sql "SELECT LoanRateTierA FROM BankSettings")" "8.99"
TS=$(tok $A /admin/settings)
curl -s -b $A -c $A -o /dev/null -d "$SET&LoanRateTierA=7.99&reason=Promo rate for tier A&__RequestVerificationToken=$TS" $B/admin/settings
check "tier A changed" "$(sql "SELECT LoanRateTierA FROM BankSettings")" "7.99"
has "the product page follows the setting" "$(curl -s $B/products/loans)" "7.99%"
TS=$(tok $A /admin/settings)
curl -s -b $A -c $A -o /dev/null -d "$SET&LoanRateTierA=8.99&reason=Back to the normal rate&__RequestVerificationToken=$TS" $B/admin/settings
check "restored" "$(sql "SELECT LoanRateTierA FROM BankSettings")" "8.99"

echo "== API"
LJ=$(curl -s -b $J $B/api/loans)
has "GET /api/loans lists Jordan's loans" "$LJ" "\"accountId\":$JAUTO"
has "and the paid-off one" "$LJ" "\"accountId\":$JOLD"
has "DTO: next payment and payoff today" "$LJ" "payoffAmountToday"
check "schedule API ends at 0" "$(curl -s -b $J $B/api/loans/$JAUTO/schedule | grep -oE '"balanceAfter":[0-9.]+' | tail -1 | cut -d: -f2)" "0.00"
check "/api/accounts: loan shows what's owed" "$(curl -s -b $J $B/api/accounts/$JAUTO | grep -oE '"amountOwed":[0-9.]+' | cut -d: -f2)" "$(money "-1 * $(bal $JAUTO)")"
has "swagger lists the loan endpoints" "$(curl -s $B/swagger/v1/swagger.json)" "/api/loans/calculator"

summary
