#!/usr/bin/env bash
# ALTO PEAK CREDIT CARD — the seed, the SIMULATED decision through the real application (approved / pending / denied,
# one card per customer), admin approval with the proposed limit, the credit limit (and simultaneous purchases), cash
# back, the statement cycle run by the scheduler (close, minimum payment math, grace period, daily-balance interest),
# paying (options, overpay blocked, PRG), late fee + admin waive, autopay success and failure, redeeming cash back,
# disputes on card purchases (refund takes the cash back too), cards kept out of transfers/deposits, admin limit change
# and close at $0, ownership 404s, the API.
# Needs B and TEST_DB ("moves time" on the scratch DB: due dates, statement close dates, backdated rows).
source "$(dirname "$0")/lib.sh"; wait_up
J=$(mktemp); demo $J customer >/dev/null                     # Jordan (paid in full every month, autopay on)
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null  # Maya (carries a balance)
S=$(mktemp); login $S sam@altobank.com 'Demo123!' >/dev/null   # Samuel (pending card application)
A=$(mktemp); demo $A admin >/dev/null
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
first_id() { curl -s -b $1 $B/api/accounts | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
bal() { sql "SELECT printf('%.2f', Balance) FROM Accounts WHERE Id = $1"; }
rew() { sql "SELECT printf('%.2f', RewardsBalance) FROM Accounts WHERE Id = $1"; }
cardof() { sql "SELECT Id FROM Accounts WHERE UserId = '$1' AND AccountType = 'CreditCard' AND Status <> 'Closed'"; }
latest() { sql "SELECT Id FROM CreditCardStatements WHERE AccountId = $1 ORDER BY PeriodEnd DESC LIMIT 1"; }
stmt() { sql "SELECT $2 FROM CreditCardStatements WHERE Id = $1"; }
nstmt() { sql "SELECT COUNT(*) FROM CreditCardStatements WHERE AccountId = $1"; }
# waitfor <name> <sql> <expected> — the scheduler runs every 5 s on the test server.
waitfor() { local i v; for i in $(seq 1 40); do v=$(sql "$2"); [ "$v" = "$3" ] && { ok "$1 ($v)"; return; }; sleep 0.5; done; bad "$1: got [$v] expected [$3]"; }
buy() { post $1 /accounts/$2 /credit-card/$2/purchase --data-urlencode "merchant=$3" -d "amount=$4"; }
pay() { local J=$1 C=$2; shift 2; post $J /credit-card/$C/pay /credit-card/$C/pay "$@"; }
range() { awk -v v="$2" -v lo="$3" -v hi="$4" 'BEGIN { exit !(v >= lo && v <= hi) }' && ok "$1 ($2)" || bad "$1: [$2] not in [$3, $4]"; }
JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM); SID=$(uid SAM@ALTOBANK.COM)
JCARD=$(cardof $JID); MCARD=$(cardof $MID)
JCHK=$(first_id $J Checking); MCHK=$(first_id $M Checking); SCHK=$(first_id $S Checking)
TODAY=$(curl -s -b $J $B/accounts/$JCARD | grep -o 'data-today="[0-9-]*"' | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}')
echo "bank today: $TODAY"

echo "== Seeded demo data"
check "Jordan and Maya each have one Alto Peak card" "$(sql "SELECT COUNT(*) FROM Accounts WHERE AccountType = 'CreditCard'")" 2
check "Jordan: 3 statements, all paid in full, no interest ever" "$(sql "SELECT COUNT(*) || ':' || printf('%.2f', SUM(CAST(InterestCharged AS REAL))) FROM CreditCardStatements WHERE AccountId = $JCARD")" "3:0.00"
check "Jordan: autopay = statement balance from checking" "$(sql "SELECT Autopay || ':' || AutopayFromAccountId FROM Accounts WHERE Id = $JCARD")" "StatementBalance:$JCHK"
check "Maya: interest on statements 2 and 3 (minimum payments only)" "$(sql "SELECT GROUP_CONCAT(CAST(InterestCharged AS REAL) > 0) FROM (SELECT InterestCharged FROM CreditCardStatements WHERE AccountId = $MCARD ORDER BY PeriodEnd)")" "0,1,1"
check "Maya: one late fee (paid after the due date)" "$(sql "SELECT COUNT(*) FROM CreditCardStatements WHERE AccountId = $MCARD AND LateFeeCharged = 1")" 1
check "Maya: the fee is on the card" "$(sql "SELECT COUNT(*) || ':' || printf('%.2f', MAX(CAST(Amount AS REAL))) FROM Transactions WHERE AccountId = $MCARD AND Type = 'LateFee'")" "1:29.00"
check "Samuel: a pending card application with a proposed limit" "$(sql "SELECT Status || ':' || (CreditLimit IS NOT NULL) FROM AccountApplications WHERE UserId = '$SID' AND AccountType = 'CreditCard'")" "Pending:1"
check "every seeded statement adds up (previous - credits + purchases + interest + fees)" "$(sql "SELECT COUNT(*) FROM CreditCardStatements WHERE ABS(CAST(PreviousBalance AS REAL) - CAST(PaymentsAndCredits AS REAL) + CAST(Purchases AS REAL) + CAST(InterestCharged AS REAL) + CAST(FeesCharged AS REAL) - CAST(StatementBalance AS REAL)) > 0.005")" 0
check "each statement starts where the last one ended" "$(sql "SELECT COUNT(*) FROM CreditCardStatements a JOIN CreditCardStatements b ON b.AccountId = a.AccountId AND b.PeriodStart = date(a.PeriodEnd, '+1 day') WHERE b.PreviousBalance <> a.StatementBalance AND ABS(CAST(b.PreviousBalance AS REAL) - CAST(a.StatementBalance AS REAL)) > 0.005")" 0
check "interest is dated on its statement's close day" "$(sql "SELECT COUNT(*) FROM Transactions t JOIN CreditCardStatements s ON s.AccountId = t.AccountId WHERE t.Type = 'InterestCharge' AND s.InterestCharged <> '0' AND date(t.Date, '-5 hours') = s.PeriodEnd")" "$(sql "SELECT COUNT(*) FROM CreditCardStatements WHERE CAST(InterestCharged AS REAL) > 0")"
check "the old monthly statement URL sends a card to its statements" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $J $B/accounts/$JCARD/statements/2026/10)" "$B/accounts/$JCARD#statements"
check "settings defaults: APR, late fee, cash back, minimum" "$(sql "SELECT CardPurchaseAprPercent || '|' || CardLateFee || '|' || CardCashBackRate || '|' || CardMinimumPaymentFloor || '|' || CardMinimumPaymentPercent FROM BankSettings")" "21.99|29|0.015|25|0.02"

echo "== Card page (customer)"
P=$(curl -s -b $J $B/accounts/$JCARD)
has "shows what's owed" "$P" "Current balance"
has "available credit" "$P" "Available credit"
has "credit used bar" "$P" 'aria-label="Credit used"'
has "minimum payment due" "$P" "Minimum payment due"
has "Pay card button" "$P" "/credit-card/$JCARD/pay"
has "Statements tab" "$P" 'data-bs-target="#statements"'
has "Rewards & autopay tab" "$P" 'data-bs-target="#rewards"'
has "Card tab (Alto Peak)" "$P" "Alto Peak ••"
hasnt "no Direct Deposit tab on a card" "$P" 'data-bs-target="#direct-deposit"'
has "rows show what's owed, not a negative balance" "$P" 'tx-bal">Owed'
has "statement links open the printable view" "$P" "/credit-card/statements/"
JS=$(latest $JCARD)
SP=$(curl -s -b $J $B/credit-card/statements/$JS)
check "printable statement 200" "$(code -b $J $B/credit-card/statements/$JS)" 200
has "statement: payment information" "$SP" "Minimum payment due"
has "statement: interest explained" "$SP" "daily balance method"
has "statement: simulated" "$SP" "not a real credit product"
AP=$(curl -s -b $J $B/accounts)
has "accounts list: card shows Owed + available" "$AP" "available of"
has "accounts list: Pay card, not Deposit/Withdraw" "$AP" "/credit-card/$JCARD/pay"
hasnt "accounts list: no deposit link for the card" "$AP" "accountId=$JCARD"
has "dashboard lists the card" "$(curl -s -b $J $B/dashboard)" "Alto Peak"
check "API: card fields" "$(curl -s -b $J $B/api/accounts/$JCARD | grep -oE '"creditLimit":[0-9.]+' )" '"creditLimit":3500'
has "public product page: apply for Alto Peak" "$(curl -s $B/products/creditcards)" "type=CreditCard"
has "public product page: demo note" "$(curl -s $B/products/creditcards)" "Demo decision, no credit check"

echo "== Ownership"
check "another customer's card page -> 404" "$(code -b $J $B/accounts/$MCARD)" 404
check "another customer's pay page -> 404" "$(code -b $J $B/credit-card/$MCARD/pay)" 404
check "another customer's statement -> 404" "$(code -b $J $B/credit-card/statements/$(latest $MCARD))" 404
MB=$(bal $MCARD)
check "purchase on another customer's card -> 404" "$(post $J /accounts/$JCARD /credit-card/$MCARD/purchase -d merchant=Sneaky -d amount=10 | cut -c1-3)" 404
check "pay another customer's card -> 404" "$(post $J /credit-card/$JCARD/pay /credit-card/$MCARD/pay -d Option=Minimum -d FromAccountId=$JCHK | cut -c1-3)" 404
check "redeem on another customer's card -> 404" "$(post $J /accounts/$JCARD /credit-card/$MCARD/redeem -d to=statement | cut -c1-3)" 404
check "autopay on another customer's card -> 404" "$(post $J /accounts/$JCARD /credit-card/$MCARD/autopay -d mode=None | cut -c1-3)" 404
check "Maya's card untouched" "$(bal $MCARD)" "$MB"
check "API: another customer's card -> 404" "$(code -b $J $B/api/accounts/$MCARD)" 404
check "anonymous -> login" "$(code $B/credit-card/$JCARD/pay)" 302
check "admin can't use customer card pages" "$(code -b $A $B/credit-card/$JCARD/pay)" 404
check "no antiforgery token -> 400" "$(code -b $J -X POST -d 'merchant=X&amount=5' $B/credit-card/$JCARD/purchase)" 400

echo "== A credit card isn't a deposit account"
JB=$(bal $JCARD)
check "transfer FROM the card refused" "$(post $J /Transactions/Transfer /Transactions/Transfer -d FromAccountId=$JCARD -d ToAccountId=$JCHK -d Amount=5 | cut -c1-3)" 200
has "with a clear message" "$(cat /tmp/post.html)" "credit card, so it can"
post $J /Transactions/Transfer /Transactions/Transfer -d FromAccountId=$JCHK -d ToAccountId=$JCARD -d Amount=5 >/dev/null
has "transfer TO the card refused (use Pay card)" "$(cat /tmp/post.html)" "Use Pay card"
post $J /Transactions/Deposit /Transactions/Deposit -d AccountId=$JCARD -d Amount=5 >/dev/null
has "deposit into the card refused" "$(cat /tmp/post.html)" "credit card, so it can"
check "card balance unchanged" "$(bal $JCARD)" "$JB"
hasnt "card not offered in the transfer form" "$(curl -s -b $J $B/Transactions/Transfer)" "value=\"$JCARD\""
hasnt "card not offered for check deposits" "$(curl -s -b $J $B/checks)" "value=\"$JCARD\""
check "API transfer from the card -> 400" "$(code -b $J -H 'Content-Type: application/json' -d "{\"fromAccountId\":$JCARD,\"toAccountId\":$JCHK,\"amount\":5}" $B/api/transfers)" 400

echo "== Cash back: earned on purchases, redeemed"
JR=$(rew $JCARD); NAUD=$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.Purchase' AND TargetId = '$JCARD'")
R=$(buy $J $JCARD "Summit Grocer" 100.00)
check "purchase -> PRG to the card" "$R" "302 $B/accounts/$JCARD"
has "says what was earned" "$(flash $J $B/accounts/$JCARD)" "You earned \$1.50 cash back"
check "owed went up by the purchase" "$(bal $JCARD)" "$(awk -v b="$JB" 'BEGIN { printf "%.2f", b - 100 }')"
check "1.5% cash back accrued" "$(rew $JCARD)" "$(awk -v r="$JR" 'BEGIN { printf "%.2f", r + 1.50 }')"
check "purchase is categorized for Insights" "$(sql "SELECT Category FROM Transactions WHERE AccountId = $JCARD ORDER BY Id DESC LIMIT 1")" "Groceries"
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.Purchase' AND TargetId = '$JCARD'")" "$((NAUD + 1))"
JRB=$(rew $JCARD); JCB=$(bal $JCHK)
post $J /accounts/$JCARD /credit-card/$JCARD/redeem -d to=$JCHK >/dev/null
has "redeem to checking" "$(flash $J $B/accounts/$JCARD)" "cash back added to"
check "checking got the cash back" "$(bal $JCHK)" "$(awk -v a="$JCB" -v r="$JRB" 'BEGIN { printf "%.2f", a + r }')"
check "rewards back to 0" "$(rew $JCARD)" "0.00"
check "a RewardRedemption row on checking" "$(sql "SELECT Type || ':' || printf('%.2f', Amount) FROM Transactions WHERE AccountId = $JCHK ORDER BY Id DESC LIMIT 1")" "RewardRedemption:$JRB"
post $J /accounts/$JCARD /credit-card/$JCARD/redeem -d to=statement >/dev/null
has "under \$10 can't be redeemed" "$(flash $J $B/accounts/$JCARD)" "once you have \$10.00 or more"

echo "== The simulated decision (real application flow)"
apply_user() {   # apply_user <jar> <email> → registers and verifies identity; sets IDTOK
  local T=$(tok $1 /Auth/Register)
  curl -s -b $1 -c $1 -o /dev/null --data-urlencode "FirstName=Riley" --data-urlencode "LastName=Stone" --data-urlencode "Email=$2" \
    --data-urlencode "Password=Passw0rd!" --data-urlencode "ConfirmPassword=Passw0rd!" --data-urlencode "__RequestVerificationToken=$T" $B/Auth/Register
  T=$(tok $1 /Accounts/Apply)
  IDTOK=$(curl -s -b $1 -c $1 -X POST -H "RequestVerificationToken: $T" $B/Accounts/VerifyIdentity | sed 's/.*"token":"\([^"]*\)".*/\1/')
}
apply() {   # apply <jar> <type> <income> [extra -d args] → "status redirect"
  local J=$1 TY=$2 INC=$3; shift 3
  post $J /Accounts/Apply /Accounts/Apply -d AccountType=$TY -d FirstName=Riley -d LastName=Stone -d DobMonth=5 -d DobDay=9 -d DobYear=1990 \
    -d Citizenship=US --data-urlencode "Phone=(814) 555-0111" --data-urlencode "Street=12 Ridge Rd" -d City=Altoona -d State=PA -d Zip=16601 \
    -d LivedTwoYears=true -d EmploymentStatus=Employed --data-urlencode "Employer=Ridge Co" -d Occupation=Planner --data-urlencode "IncomeRange=$INC" \
    -d SourceOfFunds=Employment --data-urlencode "IdentityToken=$IDTOK" --data-urlencode "QuizStreet=12 Ridge Rd" -d QuizZip=16601 \
    -d AgreeAccountAgreement=true -d AgreePrivacy=true -d AgreeESign=true --data-urlencode "ESignName=Riley Stone" "$@"
}
HIGH='$200,000 or more'   # (ASCII only: Windows mangles non-ASCII curl arguments)
U=$(mktemp); UEMAIL="peak$RANDOM$RANDOM@test.com"; apply_user $U $UEMAIL; UID_=$(uid $(echo $UEMAIL | tr a-z A-Z))
R=$(apply $U CreditCard "$HIGH" -d MonthlyHousingPayment=900 -d AgreeCardmemberAgreement=true)
APP=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$UID_'")
check "brand-new customer (no Alto deposits) -> pending review" "$(sql "SELECT Status FROM AccountApplications WHERE Id = $APP")" "Pending"
DP=$(curl -s -b $U $B/Accounts/Decision/$APP)
has "status page: why" "$DP" "new to Alto"
has "status page: demo note" "$DP" "Demo decision, no credit check"
check "no card opened" "$(cardof $UID_)" ""
R=$(apply $U Checking "$HIGH" -d InitialDeposit=5000 --data-urlencode "ExternalBankName=First Keystone" -d ExternalLast4=4321 -d CardDesign=aurora)
UCHK=$(sql "SELECT Id FROM Accounts WHERE UserId = '$UID_' AND AccountType = 'Checking' AND CAST(Balance AS REAL) > 0 ORDER BY Id DESC LIMIT 1"); echo "user $UID_ checking $UCHK"
check "opened checking with \$5,000" "$(bal $UCHK)" "5000.00"
sql "UPDATE AccountApplications SET Status = 'Denied', DenialReason = 'Test cleanup' WHERE Id = $APP" >/dev/null   # clear the pending card app
R=$(apply $U CreditCard "$HIGH" -d MonthlyHousingPayment=15000 -d AgreeCardmemberAgreement=true)
APP=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$UID_'")
check "housing over half of income -> denied" "$(sql "SELECT Status || ':' || (DenialReason LIKE '%housing payment is high%') FROM AccountApplications WHERE Id = $APP")" "Denied:1"
has "denied page shows the demo note" "$(curl -s -b $U $B/Accounts/Decision/$APP)" "Demo decision, no credit check"
R=$(apply $U CreditCard "$HIGH" -d MonthlyHousingPayment=900)
has "Cardmember Agreement required" "$(cat /tmp/post.html)" "Please agree to the Cardmember Agreement"
R=$(apply $U CreditCard "$HIGH" -d AgreeCardmemberAgreement=true)
has "housing payment required" "$(cat /tmp/post.html)" "monthly rent or mortgage"
R=$(apply $U CreditCard "$HIGH" -d MonthlyHousingPayment=900 -d AgreeCardmemberAgreement=true -d InitialDeposit=50)
APP=$(sql "SELECT MAX(Id) FROM AccountApplications WHERE UserId = '$UID_'")
check "approved by the demo rules -> decision page" "$R" "302 $B/accounts/decision/$APP"
UCARD=$(cardof $UID_)
check "card opened with the rule's limit (\$200k+ = \$4,500)" "$(sql "SELECT printf('%.2f', CreditLimit) || ':' || printf('%.2f', Balance) || ':' || CardDesign FROM Accounts WHERE Id = $UCARD")" "4500.00:0.00:peak"
check "no opening deposit taken from anywhere" "$(bal $UCHK)" "5000.00"
check "a Peak card was issued" "$(sql "SELECT COUNT(*) FROM Cards WHERE AccountId = $UCARD AND Status = 'Active'")" 1
check "statement close day + first close date set" "$(sql "SELECT StatementCloseDay BETWEEN 1 AND 28 AND NextStatementDate > '$TODAY' FROM Accounts WHERE Id = $UCARD")" 1
D=$(curl -s -b $U $B/Accounts/Decision/$APP)
has "decision: credit limit" "$D" "Credit limit"
has "decision: demo note" "$D" "Demo decision, no credit check"
has "decision: next step autopay" "$D" "Turn on autopay"
check "application stores the decision" "$(sql "SELECT Status || ':' || printf('%.0f', CreditLimit) || ':' || AgreedCardmemberAgreement || ':' || printf('%.0f', MonthlyHousingPayment) FROM AccountApplications WHERE Id = $APP")" "Approved:4500:1:900"
R=$(apply $U CreditCard "$HIGH" -d MonthlyHousingPayment=900 -d AgreeCardmemberAgreement=true)
has "one card per customer" "$(cat /tmp/post.html)" "already have an Alto Peak card"
check "still one card" "$(sql "SELECT COUNT(*) FROM Accounts WHERE UserId = '$UID_' AND AccountType = 'CreditCard'")" 1
has "apply page: card option disabled with the reason" "$(curl -s -b $U $B/Accounts/Apply)" "You already have an Alto Peak card."
has "apply page: Peak product offered" "$(curl -s -b $S $B/Accounts/Apply)" "Alto Peak Card"

echo "== Admin approves Samuel's pending application"
SAPP=$(sql "SELECT Id FROM AccountApplications WHERE UserId = '$SID' AND AccountType = 'CreditCard' AND Status = 'Pending'")
has "admin detail shows the decision + demo note" "$(curl -s -b $A $B/admin/applications/details/$SAPP)" "Demo decision, no credit check"
has "Samuel's apply page: card under review" "$(curl -s -b $S $B/Accounts/Apply)" "being reviewed"
post $A /admin/applications/details/$SAPP /admin/applications/approve/$SAPP --data-urlencode "reason=Income and housing confirmed" >/dev/null
SCARD=$(cardof $SID)
check "approved with the proposed limit" "$(sql "SELECT printf('%.2f', CreditLimit) FROM Accounts WHERE Id = $SCARD")" "$(sql "SELECT printf('%.2f', CreditLimit) FROM AccountApplications WHERE Id = $SAPP")"

echo "== Credit limit"
LIM=$(sql "SELECT printf('%.2f', CreditLimit) FROM Accounts WHERE Id = $SCARD")
HALF=$(awk -v l="$LIM" 'BEGIN { printf "%.2f", l * 0.3 }')
# 6 purchases of 30% of the limit at the same moment: only 3 fit.
for i in 1 2 3 4 5 6; do T=$(tok $S /accounts/$SCARD); echo "$T" > /tmp/cc_tok_$i; done
for i in 1 2 3 4 5 6; do curl -s -b $S -o /dev/null -w '%{http_code}\n' --data-urlencode "merchant=Race Store $i" -d "amount=$HALF" --data-urlencode "__RequestVerificationToken=$(cat /tmp/cc_tok_$i)" $B/credit-card/$SCARD/purchase > /tmp/cc_race_$i & done; wait
check "simultaneous purchases: only what fits the limit posted" "$(sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $SCARD AND Type = 'CardPurchase' AND Description LIKE 'Race Store%'")" 3
check "never past the limit" "$(sql "SELECT -CAST(Balance AS REAL) <= CAST(CreditLimit AS REAL) FROM Accounts WHERE Id = $SCARD")" 1
check "running balance matches the account" "$(sql "SELECT printf('%.2f', BalanceAfter) FROM Transactions WHERE AccountId = $SCARD ORDER BY Id DESC LIMIT 1")" "$(bal $SCARD)"
AVAIL=$(sql "SELECT printf('%.2f', CAST(CreditLimit AS REAL) + CAST(Balance AS REAL)) FROM Accounts WHERE Id = $SCARD")
SB=$(bal $SCARD)
buy $S $SCARD "Too Much Store" $(awk -v a="$AVAIL" 'BEGIN { printf "%.2f", a + 0.01 }') >/dev/null
has "a purchase over the available credit is declined" "$(flash $S $B/accounts/$SCARD)" "more than your available credit"
check "nothing posted" "$(bal $SCARD)" "$SB"
buy $S $SCARD "x" 5 >/dev/null; has "merchant required" "$(flash $S $B/accounts/$SCARD)" "Enter where you"
buy $S $SCARD "Store" -5 >/dev/null; has "negative amount refused" "$(flash $S $B/accounts/$SCARD)" "Enter the purchase amount"
check "still nothing posted" "$(bal $SCARD)" "$SB"
SPLAST=$(sql "SELECT Id FROM Cards WHERE AccountId = $SCARD AND Status = 'Active'")
sql "UPDATE Cards SET Status = 'Frozen' WHERE Id = $SPLAST" >/dev/null
buy $S $SCARD "Frozen Store" 5 >/dev/null; has "frozen card can't buy" "$(flash $S $B/accounts/$SCARD)" "is frozen"
sql "UPDATE Cards SET Status = 'Active' WHERE Id = $SPLAST" >/dev/null

echo "== Dispute on a card purchase (refund + cash back taken back)"
DTX=$(sql "SELECT Id FROM Transactions WHERE AccountId = $SCARD AND Type = 'CardPurchase' ORDER BY Id LIMIT 1")
DAMT=$(sql "SELECT printf('%.2f', Amount) FROM Transactions WHERE Id = $DTX")
check "card purchase can be disputed" "$(code -b $S "$B/disputes/new?transactionId=$DTX")" 200
TK=$(tok $S /disputes)
curl -s -b $S -c $S -o /dev/null -F "Form.TransactionId=$DTX" -F "Form.Reason=NotReceived" -F "Form.Description=The store never shipped my order and won't answer." -F "__RequestVerificationToken=$TK" $B/disputes
DID=$(sql "SELECT Id FROM Disputes WHERE TransactionId = $DTX")
check "dispute filed" "$(sql "SELECT Status FROM Disputes WHERE Id = $DID")" "Submitted"
SB=$(bal $SCARD); SR=$(rew $SCARD)
post $A /admin/disputes/details/$DID /admin/disputes/approve/$DID --data-urlencode "reason=Merchant could not show delivery" >/dev/null
check "approved" "$(sql "SELECT Status FROM Disputes WHERE Id = $DID")" "Approved"
check "refund lowers what's owed" "$(bal $SCARD)" "$(awk -v b="$SB" -v a="$DAMT" 'BEGIN { printf "%.2f", b + a }')"
check "the purchase's cash back was taken back" "$(rew $SCARD)" "$(awk -v r="$SR" -v a="$DAMT" 'BEGIN { c = int(a * 0.015 * 100 + 0.5) / 100; v = r - c; if (v < 0) v = 0; printf "%.2f", v }')"
DTX2=$(sql "SELECT Id FROM Transactions WHERE AccountId = $SCARD AND Type = 'CardPurchase' AND Status = 'Posted' ORDER BY Id LIMIT 1")
DAMT2=$(sql "SELECT printf('%.2f', Amount) FROM Transactions WHERE Id = $DTX2")
sql "UPDATE BankSettings SET ProvisionalCreditOnDisputes = 1" >/dev/null
TK=$(tok $S /disputes)
curl -s -b $S -c $S -o /dev/null -F "Form.TransactionId=$DTX2" -F "Form.Reason=NotReceived" -F "Form.Description=Second order that never came, merchant is gone." -F "__RequestVerificationToken=$TK" $B/disputes
sql "UPDATE BankSettings SET ProvisionalCreditOnDisputes = 0" >/dev/null
DID2=$(sql "SELECT Id FROM Disputes WHERE TransactionId = $DTX2")
check "provisional credit posted on the card" "$(sql "SELECT ProvisionalCreditTransactionId IS NOT NULL FROM Disputes WHERE Id = $DID2")" 1
SR=$(rew $SCARD)
post $A /admin/disputes/details/$DID2 /admin/disputes/approve/$DID2 --data-urlencode "reason=Merchant out of business" >/dev/null
check "approving a provisional credit also takes the cash back" "$(rew $SCARD)" "$(awk -v r="$SR" -v a="$DAMT2" 'BEGIN { c = int(a * 0.015 * 100 + 0.5) / 100; v = r - c; if (v < 0) v = 0; printf "%.2f", v }')"
check "schedule from a card refused" "$(post $S /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$SCARD" -d "Transfer.ToAccountId=$SCHK" -d "Transfer.Amount=5" -d "Transfer.When=once" -d "Transfer.Date=$(sql "SELECT date('$TODAY','+1 day')")" | cut -c1-3; flash $S $B/scheduled | grep -c 'use checking or savings')" "302
1"
check "nothing scheduled with a card" "$(sql "SELECT COUNT(*) FROM ScheduledPayments WHERE FromAccountId = $SCARD OR ToAccountId = $SCARD")" 0

echo "== Statement close, minimum payment math, paying"
# Riley's card: a clean history 40 days back. Purchases 35 days ago, statement 1 closes 20 days ago.
sql "UPDATE Accounts SET OpenedAt = datetime('$TODAY','-40 days','+15 hours') WHERE Id = $UCARD" >/dev/null
buy $U $UCARD "Northgate Outfitters" 1500.00 >/dev/null; buy $U $UCARD "Summit Grocer" 500.00 >/dev/null
check "cash back on \$2,000 = \$30.00" "$(rew $UCARD)" "30.00"
sql "UPDATE Transactions SET Date = datetime('$TODAY','-35 days','+16 hours') WHERE AccountId = $UCARD" >/dev/null
sql "UPDATE Accounts SET NextStatementDate = date('$TODAY','-20 days') WHERE Id = $UCARD" >/dev/null
waitfor "scheduler closed statement 1" "SELECT COUNT(*) FROM CreditCardStatements WHERE AccountId = $UCARD" 1
S1=$(latest $UCARD)
check "statement 1: balance, minimum (2% of \$2,000 = \$40), no interest on the first statement" "$(stmt $S1 "printf('%.2f|%.2f|%.2f|%.2f', StatementBalance, MinimumPayment, InterestCharged, Purchases)")" "2000.00|40.00|0.00|2000.00"
check "due 25 days after the close" "$(stmt $S1 "CAST(julianday(DueDate) - julianday(PeriodEnd) AS INTEGER)")" "25"
check "next close a month later" "$(sql "SELECT NextStatementDate = date('$TODAY','-20 days','+1 month') FROM Accounts WHERE Id = $UCARD")" 1
check "statement alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$UID_' AND Type = 'CardStatement'")" 1
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.StatementClose' AND TargetId = '$UCARD'")" 1
sleep 6; check "closing is idempotent (next scheduler runs add nothing)" "$(nstmt $UCARD)" 1
PP=$(curl -s -b $U $B/credit-card/$UCARD/pay)
has "pay page: minimum" "$PP" "\$40.00"
has "pay page: statement balance" "$PP" "\$2,000.00"
has "pay page: explains no interest" "$PP" "pay interest"
pay $U $UCARD -d Option=Custom -d CustomAmount=2500.00 -d FromAccountId=$UCHK >/dev/null
has "overpayment blocked" "$(cat /tmp/post.html)" "more than you owe"
pay $U $UCARD -d Option=Custom -d FromAccountId=$UCHK >/dev/null
has "custom needs an amount" "$(cat /tmp/post.html)" "Enter how much you want to pay"
pay $U $UCARD -d Option=Minimum -d FromAccountId=$MCHK >/dev/null
has "can't pay from someone else's account" "$(cat /tmp/post.html)" "Choose one of your checking or savings accounts"
check "nothing moved" "$(bal $UCARD):$(bal $UCHK)" "-2000.00:5000.00"
R=$(pay $U $UCARD -d Option=Minimum -d FromAccountId=$UCHK)
check "pay minimum -> PRG to the card" "$R" "302 $B/accounts/$UCARD"
has "confirmation says what's owed now" "$(flash $U $B/accounts/$UCARD)" "Paid \$40.00"
check "card and checking moved together" "$(bal $UCARD):$(bal $UCHK)" "-1960.00:4960.00"
check "linked transfer legs" "$(sql "SELECT COUNT(*) FROM Transactions a JOIN Transactions b ON b.Id = a.LinkedTransactionId WHERE a.AccountId = $UCARD AND a.Type = 'CardPayment' AND b.AccountId = $UCHK")" 1
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.Payment' AND TargetId = '$UCARD'")" 1
pay $U $UCARD -d Option=Minimum -d FromAccountId=$UCHK >/dev/null
has "minimum already paid" "$(cat /tmp/post.html)" "already paid"
has "card page: minimum shows paid" "$(curl -s -b $U $B/accounts/$UCARD)" "Paid. Thank you!"

echo "== Grace period and interest"
# The payment happened 15 days ago (before the due date) -> on time, no late fee; but only the minimum, so interest next time.
sql "UPDATE Transactions SET Date = datetime('$TODAY','-15 days','+16 hours') WHERE AccountId = $UCARD AND Type = 'CardPayment'" >/dev/null
sql "UPDATE CreditCardStatements SET DueDate = date('$TODAY','-5 days') WHERE Id = $S1" >/dev/null
waitfor "due date processed: paid on time, no late fee" "SELECT DueDateProcessed || ':' || LateFeeCharged FROM CreditCardStatements WHERE Id = $S1" "1:0"
sql "UPDATE Accounts SET NextStatementDate = date('$TODAY','-2 days') WHERE Id = $UCARD" >/dev/null
waitfor "statement 2 closed" "SELECT COUNT(*) FROM CreditCardStatements WHERE AccountId = $UCARD" 2
S2=$(latest $UCARD)
# 18 days (19 days ago .. 2 days ago): 4 at $2,000 and 14 at $1,960 x 21.99% / 365 = $21.35 (a day edge can move it 2-3 cents).
range "carried a balance -> daily-balance interest" "$(stmt $S2 "printf('%.2f', InterestCharged)")" 21.30 21.40
check "interest dated the last moment of the close day" "$(sql "SELECT date(Date, '-5 hours') FROM Transactions WHERE AccountId = $UCARD AND Type = 'InterestCharge'")" "$(stmt $S2 PeriodEnd)"
check "interest posted on the card" "$(sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $UCARD AND Type = 'InterestCharge'")" 1
check "statement 2 math: previous - payments + interest = balance" "$(stmt $S2 "printf('%.2f', PreviousBalance - PaymentsAndCredits + Purchases + InterestCharged + FeesCharged) = printf('%.2f', StatementBalance)")" 1
check "statement 2 minimum = max(\$25, 2%)" "$(stmt $S2 "printf('%.2f', MinimumPayment) = printf('%.2f', MAX(25, ROUND(StatementBalance * 0.02, 2)))")" 1
MX=$(sql "SELECT MAX(Id) FROM Transactions")
post $U /accounts/$UCARD /credit-card/$UCARD/redeem -d to=statement >/dev/null
has "redeem \$30 as a statement credit" "$(flash $U $B/accounts/$UCARD)" "\$30.00 cash back added to your card balance"
check "a CashBackCredit on the card" "$(sql "SELECT Type || ':' || printf('%.2f', Amount) FROM Transactions WHERE AccountId = $UCARD ORDER BY Id DESC LIMIT 1")" "CashBackCredit:30.00"
R=$(pay $U $UCARD -d Option=StatementBalance -d FromAccountId=$UCHK)
check "pay the statement balance (less the credit already applied)" "$(bal $UCARD)" "0.00"
sql "UPDATE Transactions SET Date = datetime('$TODAY','-1 days','+17 hours') WHERE AccountId = $UCARD AND Id > $MX" >/dev/null   # those happened yesterday
sql "UPDATE Accounts SET NextStatementDate = '$TODAY' WHERE Id = $UCARD" >/dev/null
sleep 6; check "a statement doesn't close until its close day is over" "$(nstmt $UCARD)" 2
sql "UPDATE Accounts SET NextStatementDate = date('$TODAY','-1 days') WHERE Id = $UCARD" >/dev/null
waitfor "statement 3 closed" "SELECT COUNT(*) FROM CreditCardStatements WHERE AccountId = $UCARD" 3
S3=$(latest $UCARD)
check "paid in full by the due date -> no interest (grace period back)" "$(stmt $S3 "printf('%.2f', InterestCharged)")" "0.00"
check "nothing owed -> no minimum" "$(stmt $S3 "printf('%.2f|%.2f', StatementBalance, MinimumPayment)")" "0.00|0.00"
sql "UPDATE Accounts SET RewardsBalance = '12.00' WHERE Id = $UCARD" >/dev/null   # test-only: cash back on a paid-off card
post $U /accounts/$UCARD /credit-card/$UCARD/redeem -d to=statement >/dev/null
has "a statement credit can't leave money on the card" "$(flash $U $B/accounts/$UCARD)" "Redeem to checking or savings instead"
check "nothing changed" "$(bal $UCARD):$(rew $UCARD)" "0.00:12.00"
sql "UPDATE Accounts SET RewardsBalance = '0' WHERE Id = $UCARD" >/dev/null
pay $U $UCARD -d Option=CurrentBalance -d FromAccountId=$UCHK >/dev/null
has "paid off: nothing to pay" "$(cat /tmp/post.html)" "nothing to pay"
has "card page: Pay card disabled when nothing is owed" "$(curl -s -b $U $B/accounts/$UCARD)" 'title="Nothing to pay"'

echo "== Payment due reminder (Maya, no autopay)"
MS=$(latest $MCARD); NREM=$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$MID' AND Type = 'CardPaymentDue'")   # the seeded history has some
sql "UPDATE CreditCardStatements SET DueDate = date('$TODAY','+3 days') WHERE Id = $MS" >/dev/null
waitfor "reminder 5 days before the due date" "SELECT COUNT(*) FROM Alerts WHERE UserId = '$MID' AND Type = 'CardPaymentDue'" $((NREM + 1))
sleep 6; check "only once per statement" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$MID' AND Type = 'CardPaymentDue'")" $((NREM + 1))

echo "== Late fee + admin waive (Maya)"
MS=$(latest $MCARD); MB=$(bal $MCARD); NFEE=$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$MID' AND Type = 'CardLateFee'")
sql "UPDATE CreditCardStatements SET DueDate = date('$TODAY','-2 days') WHERE Id = $MS" >/dev/null
waitfor "minimum not paid by the due date -> late fee" "SELECT LateFeeCharged FROM CreditCardStatements WHERE Id = $MS" 1
check "fee added to what's owed" "$(bal $MCARD)" "$(awk -v b="$MB" 'BEGIN { printf "%.2f", b - 29 }')"
check "late fee alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$MID' AND Type = 'CardLateFee' AND Message LIKE '%late fee was added%'")" "$((NFEE + 1))"
check "only once" "$(sleep 6; sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $MCARD AND Type = 'LateFee'")" 2
has "card page: past-due banner" "$(curl -s -b $M $B/accounts/$MCARD)" "Pay it now to bring the card up to date"
check "admin credit cards page" "$(code -b $A $B/admin/creditcards)" 200
has "admin nav has Credit Cards" "$(curl -s -b $A $B/admin)" 'href="/admin/creditcards"'
AD=$(curl -s -b $A $B/admin/creditcards/details/$MCARD)
has "admin detail: statements with a Waive button" "$AD" "#waive$MS"
post $A /admin/creditcards/details/$MCARD /admin/creditcards/waivefee/$MCARD -d statementId=$MS --data-urlencode "reason=" >/dev/null
check "waive needs a reason" "$(stmt $MS LateFeeWaived)" 0
post $A /admin/creditcards/details/$MCARD /admin/creditcards/waivefee/$MCARD -d statementId=$MS --data-urlencode "reason=First late payment, courtesy waiver" >/dev/null
check "waived" "$(stmt $MS LateFeeWaived)" 1
check "fee credited back" "$(bal $MCARD)" "$MB"
check "a FeeWaiver row" "$(sql "SELECT Type || ':' || printf('%.2f', Amount) FROM Transactions WHERE AccountId = $MCARD ORDER BY Id DESC LIMIT 1")" "FeeWaiver:29.00"
check "audited with the reason" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.WaiveFee' AND Reason LIKE 'First late payment%'")" 1
post $A /admin/creditcards/details/$MCARD /admin/creditcards/waivefee/$MCARD -d statementId=$MS --data-urlencode "reason=Again" >/dev/null
has "can't waive twice" "$(flash $A $B/admin/creditcards/details/$MCARD)" "already waived"
check "still one waiver" "$(sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $MCARD AND Type = 'FeeWaiver'")" 1
check "the waived fee is marked reversed" "$(sql "SELECT Status FROM Transactions WHERE Id = (SELECT LateFeeTransactionId FROM CreditCardStatements WHERE Id = $MS)")" "Reversed"
OLDFEE=$(sql "SELECT LateFeeTransactionId FROM CreditCardStatements WHERE AccountId = $MCARD AND LateFeeCharged = 1 AND LateFeeWaived = 0 LIMIT 1")
post $A /admin/transactions/details/$OLDFEE /admin/transactions/reverse/$OLDFEE --data-urlencode "reason=Trying the other way" >/dev/null
has "a late fee can't be reversed (only waived, so never twice)" "$(flash $A $B/admin/transactions/details/$OLDFEE)" "waived on the Credit Cards page"
check "no reversal posted" "$(sql "SELECT COUNT(*) FROM Transactions WHERE ReversalOfTransactionId = $OLDFEE")" 0

echo "== Autopay"
JS=$(latest $JCARD); JCB=$(bal $JCHK); JB=$(bal $JCARD)
DUE=$(stmt $JS "printf('%.2f', StatementBalance)")
sql "UPDATE CreditCardStatements SET DueDate = '$TODAY' WHERE Id = $JS" >/dev/null
waitfor "autopay ran on the due date" "SELECT AutopayAttemptedAt IS NOT NULL FROM CreditCardStatements WHERE Id = $JS" 1
check "paid the statement balance from checking" "$(bal $JCHK)" "$(awk -v a="$JCB" -v d="$DUE" 'BEGIN { printf "%.2f", a - d }')"
check "card owed went down by it" "$(bal $JCARD)" "$(awk -v a="$JB" -v d="$DUE" 'BEGIN { printf "%.2f", a + d }')"
check "an Autopay payment row" "$(sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $JCARD AND Type = 'CardPayment' AND Description LIKE 'Autopay from%'")" 1
check "alert: autopay made" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Card autopay made'")" 1
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.Autopay' AND TargetId = '$JCARD'")" 1
sleep 6; check "only once" "$(sql "SELECT COUNT(*) FROM Transactions WHERE AccountId = $JCARD AND Type = 'CardPayment' AND Description LIKE 'Autopay from%'")" 1
# Failure: Samuel's autopay from a checking account staff froze.
post $S /accounts/$SCARD /credit-card/$SCARD/autopay -d mode=Minimum >/dev/null
has "autopay needs a pay-from account" "$(flash $S $B/accounts/$SCARD)" "Choose the checking or savings account"
post $S /accounts/$SCARD /credit-card/$SCARD/autopay -d mode=Minimum -d fromAccountId=$MCHK >/dev/null
check "can't autopay from someone else's account" "$(sql "SELECT Autopay FROM Accounts WHERE Id = $SCARD")" "None"
post $S /accounts/$SCARD /credit-card/$SCARD/autopay -d mode=Minimum -d fromAccountId=$SCHK >/dev/null
check "autopay saved" "$(sql "SELECT Autopay || ':' || AutopayFromAccountId FROM Accounts WHERE Id = $SCARD")" "Minimum:$SCHK"
# Samuel's card activity so far happened over the last few days (so it's on the statement that closed yesterday).
sql "UPDATE Accounts SET OpenedAt = datetime('$TODAY','-5 days','+15 hours') WHERE Id = $SCARD" >/dev/null
sql "UPDATE Transactions SET Date = datetime('$TODAY','-2 days','+17 hours') WHERE AccountId = $SCARD" >/dev/null
sql "UPDATE Accounts SET NextStatementDate = date('$TODAY','-1 days') WHERE Id = $SCARD" >/dev/null
waitfor "Samuel's statement closed" "SELECT COUNT(*) FROM CreditCardStatements WHERE AccountId = $SCARD" 1
post $A /admin/accounts/details/$SCHK /admin/accounts/freeze/$SCHK --data-urlencode "reason=Test: autopay source frozen" >/dev/null
SS=$(latest $SCARD); SB=$(bal $SCARD)
sql "UPDATE CreditCardStatements SET DueDate = '$TODAY' WHERE Id = $SS" >/dev/null
waitfor "autopay attempted" "SELECT AutopayAttemptedAt IS NOT NULL FROM CreditCardStatements WHERE Id = $SS" 1
check "failed: nothing moved" "$(bal $SCARD)" "$SB"
check "the reason is kept" "$(sql "SELECT AutopayResult LIKE '%frozen%' FROM CreditCardStatements WHERE Id = $SS")" 1
check "alert: autopay failed, pay the minimum" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$SID' AND Title = 'Card autopay failed' AND Message LIKE '%avoid a late fee%'")" 1
post $A /admin/accounts/details/$SCHK /admin/accounts/unfreeze/$SCHK --data-urlencode "reason=Test done" >/dev/null

echo "== Admin: credit limit and closing"
post $A /admin/creditcards/details/$SCARD /admin/creditcards/changelimit/$SCARD -d limit=$(sql "SELECT printf('%.2f', MAX(500, -CAST(Balance AS REAL) - 10)) FROM Accounts WHERE Id = $SCARD") --data-urlencode "reason=Lower risk" >/dev/null
has "limit can't go below what's owed" "$(flash $A $B/admin/creditcards/details/$SCARD)" "lower than what"
post $A /admin/creditcards/details/$SCARD /admin/creditcards/changelimit/$SCARD -d limit=3000 --data-urlencode "reason=Good payment history" >/dev/null
check "limit changed" "$(sql "SELECT printf('%.2f', CreditLimit) FROM Accounts WHERE Id = $SCARD")" "3000.00"
check "audited with before/after" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.LimitChange' AND TargetId = '$SCARD' AND Reason = 'Good payment history'")" 1
check "customer alerted" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$SID' AND Title = 'Credit limit changed'")" 1
check "the generic account page sends staff to the card page" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $A $B/admin/accounts/details/$SCARD)" "$B/admin/creditcards/details/$SCARD"
SB=$(bal $SCARD)
post $A /admin/creditcards/details/$SCARD /admin/accounts/adjust/$SCARD -d direction=credit -d amount=50 --data-urlencode "reason=Goodwill" >/dev/null
has "no raw balance adjustments on a card" "$(flash $A $B/admin/creditcards/details/$SCARD)" "Use Credit Cards in the menu"
post $A /admin/creditcards/details/$SCARD /admin/accounts/close/$SCARD --data-urlencode "reason=Generic close" >/dev/null
check "the generic close can't close a card that's owed" "$(sql "SELECT Status FROM Accounts WHERE Id = $SCARD"):$(bal $SCARD)" "Active:$SB"
post $A /admin/creditcards/details/$SCARD /admin/accounts/freeze/$SCARD --data-urlencode "reason=Suspicious purchases" >/dev/null
buy $S $SCARD "Frozen Account Store" 5 >/dev/null; has "frozen card account can't buy" "$(flash $S $B/accounts/$SCARD)" "is frozen"
has "card page: frozen banner says payments still work" "$(curl -s -b $S $B/accounts/$SCARD)" "You can still make payments"
post $A /admin/creditcards/details/$SCARD /admin/accounts/unfreeze/$SCARD --data-urlencode "reason=Customer confirmed" >/dev/null
check "unfrozen" "$(sql "SELECT Status FROM Accounts WHERE Id = $SCARD")" "Active"
post $A /admin/creditcards/details/$SCARD /admin/creditcards/close/$SCARD --data-urlencode "reason=Customer asked" >/dev/null
has "can't close while money is owed" "$(flash $A $B/admin/creditcards/details/$SCARD)" "only be closed at \$0"
echo "== Credits count toward the minimum; refunding a credit balance (Samuel)"
OWED=$(sql "SELECT printf('%.2f', -CAST(Balance AS REAL)) FROM Accounts WHERE Id = $SCARD")
post $A /admin/accounts/details/$SCHK /admin/accounts/adjust/$SCHK -d direction=credit -d amount=1000 --data-urlencode "reason=Test: payday" >/dev/null   # Samuel needs money to pay the card
pay $S $SCARD -d Option=CurrentBalance -d FromAccountId=$SCHK >/dev/null
check "Samuel paid off the card" "$(bal $SCARD)" "0.00"
RTX=$(sql "SELECT Id FROM Transactions WHERE AccountId = $SCARD AND Type = 'CardPurchase' AND Status = 'Posted' ORDER BY Id LIMIT 1")
RAMT=$(sql "SELECT printf('%.2f', Amount) FROM Transactions WHERE Id = $RTX")
post $A /admin/transactions/details/$RTX /admin/transactions/reverse/$RTX --data-urlencode "reason=Merchant refund" >/dev/null
check "a refund after paying off leaves a credit on the card" "$(bal $SCARD)" "$RAMT"
has "card page explains the credit" "$(curl -s -b $S $B/accounts/$SCARD)" "credit on the card"
has "card page: nothing due" "$(curl -s -b $S $B/accounts/$SCARD)" 'title="Nothing to pay"'
post $A /admin/creditcards/details/$SCARD /admin/creditcards/close/$SCARD --data-urlencode "reason=Customer asked" >/dev/null
has "can't close with a credit on it" "$(flash $A $B/admin/creditcards/details/$SCARD)" "Refund it before closing"
SCB=$(bal $SCHK)
post $A /admin/creditcards/details/$SCARD /admin/creditcards/refundcredit/$SCARD -d moveTo=$MCHK --data-urlencode "reason=Send the credit back" >/dev/null
has "only to the same customer's account" "$(flash $A $B/admin/creditcards/details/$SCARD)" "same customer"
post $A /admin/creditcards/details/$SCARD /admin/creditcards/refundcredit/$SCARD -d moveTo=$SCHK --data-urlencode "reason=Send the credit back" >/dev/null
check "credit refunded to checking" "$(bal $SCARD):$(bal $SCHK)" "0.00:$(awk -v a="$SCB" -v r="$RAMT" 'BEGIN { printf "%.2f", a + r }')"
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Card.RefundCredit' AND TargetId = '$SCARD'")" 1
post $A /admin/creditcards/details/$UCARD /admin/creditcards/close/$UCARD --data-urlencode "reason=Customer asked to close" >/dev/null
check "closed at \$0" "$(sql "SELECT Status FROM Accounts WHERE Id = $UCARD")" "Closed"
check "the plastic is cancelled" "$(sql "SELECT COUNT(*) FROM Cards WHERE AccountId = $UCARD AND Status <> 'Cancelled'")" 0
check "no more statements scheduled" "$(sql "SELECT NextStatementDate IS NULL FROM Accounts WHERE Id = $UCARD")" 1
buy $U $UCARD "After Close" 5 >/dev/null; has "closed card can't buy" "$(flash $U $B/accounts/$UCARD)" "closed"
check "not deleted (history kept)" "$(sql "SELECT COUNT(*) > 5 FROM Transactions WHERE AccountId = $UCARD")" 1
ST=$(curl -s -b $A $B/admin/settings)
has "admin settings: card APR" "$ST" 'name="CardPurchaseAprPercent"'
has "admin settings: late fee" "$ST" 'name="CardLateFee"'
has "admin settings: cash back" "$ST" 'name="CardCashBackPercent"'

summary
