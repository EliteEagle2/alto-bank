#!/usr/bin/env bash
# SPENDING INSIGHTS + BUDGETS — automatic categories (and the backfill), recategorizing (page form + JSON API),
# the Insights page and /api/insights, budgets (add / change / remove, soft delete), budget alerts at 75% / 100%
# (once per month), the dashboard widget, ownership checks. Needs B and TEST_DB (reads the scratch DB).
source "$(dirname "$0")/lib.sh"; wait_up
J='Content-Type: application/json'
C=$(mktemp); demo $C customer >/dev/null                     # Jordan
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
S=$(mktemp); login $S sam@altobank.com 'Demo123!' >/dev/null
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
first_id() { curl -s -b $1 $B/api/accounts | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
cat_of() { sql "SELECT Category || ':' || CategoryOverridden FROM Transactions WHERE Id = $1"; }
spent_in() { curl -s -b $1 $B/api/insights/summary | node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{const c=JSON.parse(s).categories.find(x=>x.key==='$2');console.log((c?c.amount:0).toFixed(2))})"; }
recat() { curl -s -b $1 -o /tmp/recat.json -w '%{http_code}' -X POST -H "$J" -d "{\"category\":\"$3\"}" $B/api/insights/transactions/$2/category; }
withdraw() { post $1 /transactions/withdraw /transactions/withdraw -d "AccountId=$2&Amount=$3" --data-urlencode "Description=$4" >/dev/null; }
JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM)
JCHK=$(first_id $C Checking)
MONTH=$(curl -s -b $C $B/api/insights/summary | grep -oE '"month":"[0-9-]+"' | cut -d'"' -f4)

echo "== Automatic categories (keyword rules + backfill of the seeded history)"
rule() { check "$1 -> $3" "$(sql "SELECT DISTINCT Category FROM Transactions WHERE Description $2 AND CategoryOverridden = 0")" "$3"; }
rule "Peak Coffee Co." "= 'Peak Coffee Co.'" FoodAndDining
rule "Main St. Diner" "= 'Main St. Diner'" FoodAndDining
rule "Summit Grocers" "= 'Summit Grocers'" Groceries
rule "FreshWay Market" "= 'FreshWay Market'" Groceries
rule "Keystone Fuel" "= 'Keystone Fuel'" Gas
rule "Ridgeway Gas" "= 'Ridgeway Gas'" Gas
rule "Online order - ShopNest" "= 'Online order - ShopNest'" Shopping
rule "Streamly subscription" "= 'Streamly subscription'" Entertainment
rule "Rent payment" "= 'Rent payment'" BillsAndUtilities
rule "Electric bill" "= 'Electric bill'" BillsAndUtilities
rule "Payroll (Keystone Logistics: no 'Gas'/'Shopping' false match)" "LIKE 'Payroll - Keystone%'" Income
rule "Interest earned" "= 'Interest earned'" Income
check "every transfer leg is Transfers (except paying your own card or loan)" "$(sql "SELECT COUNT(*) FROM Transactions t WHERE Type IN ('TransferIn','TransferOut') AND Category <> 'Transfers' AND NOT (Type = 'TransferOut' AND Category = 'DebtPayments' AND CounterpartyAccountId IN (SELECT Id FROM Accounts WHERE AccountType IN ('CreditCard','Loan')))")" 0
check "paying your own card or loan is Debt payments (both sides)" "$(sql "SELECT COUNT(*) FROM Transactions WHERE Type IN ('CardPayment','LoanPayment') AND Category <> 'DebtPayments'")" 0
check "interest charged and late fees are Fees & Interest" "$(sql "SELECT COUNT(*) FROM Transactions WHERE Type IN ('InterestCharge','LateFee','FeeWaiver') AND CategoryOverridden = 0 AND Category <> 'FeesAndInterest'")" 0
check "no seeded card purchase is left in Other" "$(sql "SELECT COUNT(*) FROM Transactions WHERE Type = 'CardPurchase' AND Category = 'Other'")" 0
rule "Bookshop" "= 'Harbor Bookshop'" Shopping
rule "Fitness" "= 'Alpine Fitness Club'" Entertainment
rule "Parking" "= 'Trailhead Parking'" Gas
rule "Hardware" "= 'Peak Hardware Supply'" Shopping
rule "Dry cleaning" "= 'Cedar Dry Cleaning'" Shopping
check "every bill payment is Bills & Utilities" "$(sql "SELECT COUNT(*) FROM Transactions WHERE Type = 'BillPayment' AND Category <> 'BillsAndUtilities'")" 0
check "P2P received is Transfers" "$(sql "SELECT COUNT(*) FROM Transactions WHERE Type = 'PaymentReceived' AND Category <> 'Transfers'")" 0
check "reversals keep the category of what they undo" "$(sql "SELECT COUNT(*) FROM Transactions r JOIN Transactions o ON r.ReversalOfTransactionId = o.Id WHERE r.Category <> o.Category")" 0
check "Food & Dining is stored (enum 0 isn't swapped for the DB default)" "$([ "$(sql "SELECT COUNT(*) FROM Transactions WHERE Category = 'FoodAndDining'")" -gt 0 ] && echo yes)" yes
check "seeded: Jordan's concert tickets moved to Entertainment by him" "$(sql "SELECT Category || ':' || CategoryOverridden FROM Transactions t JOIN Accounts a ON a.Id = t.AccountId WHERE a.UserId = '$JID' AND t.Description LIKE '%Concert%' AND t.Type = 'PaymentSent'")" "Entertainment:1"
withdraw $C $JCHK 4.25 "Peak Coffee Co."
NEW=$(sql "SELECT MAX(Id) FROM Transactions WHERE AccountId = $JCHK")
check "new withdrawal is categorized when it's made" "$(cat_of $NEW)" "FoodAndDining:0"
withdraw $C $JCHK 12.00 "Something unusual"
check "no rule matches -> Other" "$(cat_of $(sql "SELECT MAX(Id) FROM Transactions WHERE AccountId = $JCHK"))" "Other:0"

echo "== Insights page"
P=$(curl -s -b $C "$B/insights")
check "/insights 200" "$(code -b $C $B/insights)" 200
for s in categoryChart trendChart merchantsTitle budgetsTitle monthSelect; do has "has #$s" "$P" "id=\"$s\""; done
has "sidebar: Insights link" "$P" 'href="/insights"'
has "sidebar: Budgets link" "$P" 'href="/budgets"'
has "spending headline" "$P" "Spent in"
has "compares with the same days last month" "$P" "than [A-Z][a-z][a-z] 1"
has "help: How Insights works" "$P" "How Insights works"
has "legend links to a category drill-down" "$P" 'category=Groceries#drilldown'
hasnt "Transfers is never a spending slice" "$P" 'category=Transfers#drilldown'
hasnt "Income is never a spending slice" "$P" 'category=Income#drilldown'
D=$(curl -s -b $C "$B/insights?category=Shopping")
has "drill-down section" "$D" 'id="drilldown"'
has "drill-down rows have a category picker" "$D" 'data-drill-cat'
check "unknown category ignored (no drill-down)" "$(curl -s -b $C "$B/insights?category=Nope" | grep -c 'id="drilldown"')" 0
check "garbage month -> this month" "$(curl -s -b $C "$B/insights?month=banana" | grep -c "value=\"$MONTH\" selected")" 1
check "future month clamps to this month" "$(curl -s -b $C "$B/insights?month=2099-01" | grep -c "value=\"$MONTH\" selected")" 1
check "anonymous -> login" "$(code $B/insights)" 302

echo "== /api/insights"
SUM=$(curl -s -b $C $B/api/insights/summary)
check "summary 200" "$(code -b $C $B/api/insights/summary)" 200
check "summary month" "$(json_field "$SUM" month)" "$MONTH"
check "category amounts add up to total spending" "$(printf '%s' "$SUM" | node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{const j=JSON.parse(s);console.log(Math.abs(j.categories.reduce((a,c)=>a+c.amount,0)-j.spending)<0.005?'yes':'no')})")" yes
check "no Income/Transfers in spending categories" "$(printf '%s' "$SUM" | grep -cE '"key":"(Income|Transfers)"')" 0
check "top merchants: at most 5" "$(printf '%s' "$SUM" | node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>console.log(JSON.parse(s).topMerchants.length<=5?'yes':'no'))")" yes
has "callouts present" "$SUM" '"callouts":\[{'
has "budgets in the summary" "$SUM" '"state":"over"'
T=$(curl -s -b $C "$B/api/insights/trend?month=$MONTH")
has "trend ends with the chosen month" "$T" "\"month\":\"$MONTH\""
check "trend: at most 6 months" "$(printf '%s' "$T" | grep -o '"month"' | wc -l | awk '{print ($1<=6)?"yes":"no"}')" yes
check "bad month -> 400" "$(code -b $C "$B/api/insights/summary?month=13-2026")" 400
check "month before the first account clamps (200)" "$(code -b $C "$B/api/insights/summary?month=2001-01")" 200
check "categories list has all 11" "$(curl -s -b $C $B/api/insights/categories | grep -o '"key"' | wc -l | tr -d ' ')" 11
check "anonymous API -> 401" "$(code $B/api/insights/summary)" 401
check "Maya sees her own numbers, not Jordan's" "$( [ "$(json_field "$(curl -s -b $M $B/api/insights/summary)" spending)" != "$(json_field "$SUM" spending)" ] && echo yes)" yes

echo "== Recategorize (JSON API)"
COFFEE=$NEW
BEFORE_G=$(spent_in $C Groceries)
check "move coffee to Groceries -> 200" "$(recat $C $COFFEE Groceries)" 200
check "stored + marked as the customer's pick" "$(cat_of $COFFEE)" "Groceries:1"
has "response has the label" "$(cat /tmp/recat.json)" '"label":"Groceries","overridden":true'
check "Groceries spending went up by 4.25" "$(awk -v a=$BEFORE_G -v b=$(spent_in $C Groceries) 'BEGIN{printf "%.2f", b-a}')" "4.25"
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Transaction.Recategorize' AND TargetId = '$COFFEE'")" 1
check "'auto' -> back to the rule's category" "$(recat $C $COFFEE auto)" 200
check "back to Food & Dining, not overridden" "$(cat_of $COFFEE)" "FoodAndDining:0"
check "unknown category -> 400" "$(recat $C $COFFEE Pizza)" 400
check "numeric category -> 400" "$(recat $C $COFFEE 99)" 400
check "combined categories (\"Groceries,Gas\") -> 400" "$(recat $C $COFFEE "Groceries,Gas")" 400
check "empty body -> 400" "$(curl -s -o /dev/null -w '%{http_code}' -b $C -X POST -H "$J" -d '{}' $B/api/insights/transactions/$COFFEE/category)" 400
MAYATX=$(sql "SELECT t.Id FROM Transactions t JOIN Accounts a ON a.Id = t.AccountId WHERE a.UserId = '$MID' LIMIT 1")
check "Maya's transaction -> 404 (IDOR)" "$(recat $C $MAYATX Groceries)" 404
check "Maya's transaction unchanged" "$(sql "SELECT CategoryOverridden FROM Transactions WHERE Id = $MAYATX")" 0
check "missing transaction -> 404" "$(recat $C 999999 Groceries)" 404
check "anonymous -> 401" "$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "$J" -d '{"category":"Gas"}' $B/api/insights/transactions/$COFFEE/category)" 401

echo "== Recategorize (page form, antiforgery + PRG)"
# (pre-encoded: Git Bash would turn a "returnUrl=/accounts/..." argument into a Windows file path)
R=$(post $C /accounts/$JCHK /transactions/$COFFEE/category -d category=Entertainment -d "returnUrl=%2Faccounts%2F$JCHK%23activity")
check "form post redirects back to the account" "$R" "302 $B/accounts/$JCHK#activity"
check "saved" "$(cat_of $COFFEE)" "Entertainment:1"
has "success message" "$(flash $C $B/accounts/$JCHK)" "Moved to Entertainment"
check "no antiforgery token -> 400" "$(code -b $C -X POST -d category=Gas $B/transactions/$COFFEE/category)" 400
check "Maya's transaction via the form -> 404" "$(post $C /accounts/$JCHK /transactions/$MAYATX/category -d category=Gas | cut -d' ' -f1)" 404
R=$(post $C /accounts/$JCHK /transactions/$COFFEE/category -d category=Gas --data-urlencode "returnUrl=https://evil.example/")
check "outside returnUrl ignored (stays on Alto)" "$R" "302 $B/insights"
recat $C $COFFEE auto >/dev/null
A=$(curl -s -b $C $B/accounts/$JCHK)
has "rows show their category" "$A" 'data-cat-chip'
has "expanded row has the category form" "$A" 'data-tx-cat'
has "activity API includes the category" "$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?pageSize=1")" '"categoryLabel":'

echo "== Budgets page"
BP=$(curl -s -b $C $B/budgets)
check "/budgets 200" "$(code -b $C $B/budgets)" 200
has "shows over budget" "$BP" "Over by"
has "shows near limit" "$BP" "% used"
has "shows on-track budgets" "$BP" "left</div>"
has "explains the colors" "$BP" "amber from 75%"
has "last-month hint data for the form" "$BP" 'data-last='
check "already-budgeted categories aren't offered again" "$(printf '%s' "$BP" | grep -c '<option value="Shopping"')" 0
check "Income can't be budgeted (not offered)" "$(printf '%s' "$BP" | grep -c '<option value="Income"')" 0
check "anonymous -> login" "$(code $B/budgets)" 302

echo "== Add / change / remove"
bs() { post $1 /budgets /budgets -d "Form.Category=$2" -d "Form.MonthlyLimit=$3"; }
check "add Bills & Utilities budget -> redirect" "$(bs $C BillsAndUtilities 2500)" "302 $B/budgets"
has "confirmation" "$(flash $C $B/budgets)" "Bills &amp; Utilities budget set to \$2,500.00 a month"
BID=$(sql "SELECT Id FROM Budgets WHERE UserId = '$JID' AND Category = 'BillsAndUtilities'")
check "stored" "$(sql "SELECT MonthlyLimit || ':' || Status FROM Budgets WHERE Id = $BID")" "2500:Active"
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Budget.Save' AND TargetId = '$BID'")" 1
bs $C BillsAndUtilities 2000 >/dev/null
has "change limit" "$(flash $C $B/budgets)" "changed from \$2,500.00 to \$2,000.00"
check "still one row per category" "$(sql "SELECT COUNT(*) FROM Budgets WHERE UserId = '$JID' AND Category = 'BillsAndUtilities'")" 1
bs $C Income 100 >/dev/null;           has "Income refused" "$(cat /tmp/post.html)" "isn&#x27;t spending"
bs $C Transfers 100 >/dev/null;        has "Transfers refused" "$(cat /tmp/post.html)" "isn&#x27;t spending"
bs $C 99 100 >/dev/null;               check "made-up category stores nothing" "$(sql "SELECT COUNT(*) FROM Budgets WHERE Category NOT IN ('FoodAndDining','Groceries','Gas','Shopping','BillsAndUtilities','Entertainment','FeesAndInterest','Other')")" 0
bs $C Other 0 >/dev/null;              has "zero limit refused" "$(cat /tmp/post.html)" "between \$1.00 and \$100,000"
bs $C Other 100000.01 >/dev/null;      has "too-large limit refused" "$(cat /tmp/post.html)" "between \$1.00 and \$100,000"
bs $C Other 10.005 >/dev/null;         has "3 decimals refused" "$(cat /tmp/post.html)" "at most 2 decimal places"
bs $C "" 50 >/dev/null;                has "no category refused" "$(cat /tmp/post.html)" "Choose a category"
bs $C Shopping 0 >/dev/null
has "a bad Change limit shows at the top of the page" "$(flash $C $B/budgets)" "Shopping budget not changed"
check "no antiforgery token -> 400" "$(code -b $C -X POST -d 'Form.Category=Gas&Form.MonthlyLimit=5' $B/budgets)" 400
check "remove -> redirect" "$(post $C /budgets /budgets/$BID/remove)" "302 $B/budgets"
check "soft delete: row kept, Removed" "$(sql "SELECT Status FROM Budgets WHERE Id = $BID")" "Removed"
hasnt "gone from the page" "$(curl -s -b $C $B/budgets)" "budgets/$BID/remove"
post $C /budgets /budgets/$BID/remove >/dev/null
has "removing twice" "$(flash $C $B/budgets)" "already removed"
bs $C BillsAndUtilities 1800 >/dev/null
check "adding it again brings back the same row" "$(sql "SELECT Id || ':' || Status || ':' || MonthlyLimit FROM Budgets WHERE UserId = '$JID' AND Category = 'BillsAndUtilities'")" "$BID:Active:1800"
MB=$(sql "SELECT Id FROM Budgets WHERE UserId = '$MID' LIMIT 1")
check "removing Maya's budget -> 404 (IDOR)" "$(post $C /budgets /budgets/$MB/remove | cut -d' ' -f1)" 404
check "Maya's budget untouched" "$(sql "SELECT Status FROM Budgets WHERE Id = $MB")" "Active"
check "budgets API" "$(curl -s -b $C $B/api/insights/budgets | grep -c "\"id\":$BID,")" 1

echo "== Budget alerts (75% and 100%, once each per month)"
check "seeded: Jordan got an over-budget alert" "$([ "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Type = 'BudgetNearLimit' AND DedupeKey LIKE 'budget:%:100'")" -ge 1 ] && echo yes)" yes
check "seeded: Maya muted budget alerts, so hers arrive already read" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$MID' AND Type = 'BudgetNearLimit' AND ReadAt IS NULL")" 0
# Sam's Entertainment: a category he has spent (almost) nothing on this month, so the limit below starts under 75%
# no matter what the earlier suites did. Limit = spent + $100; +$80 crosses 75%, +$30 more goes over.
SAMID=$(uid SAM@ALTOBANK.COM); SCHK=$(first_id $S Checking)
post $S /transactions/deposit /transactions/deposit -d "AccountId=$SCHK&Amount=300" --data-urlencode "Description=Payroll - bonus" >/dev/null
ENT=$(spent_in $S Entertainment)
check "precondition: Sam's Entertainment spending is small" "$(awk -v s=$ENT 'BEGIN{print (s < 300) ? "yes" : "no"}')" yes
post $S /budgets /budgets -d "Form.Category=Entertainment" -d "Form.MonthlyLimit=$(awk -v s=$ENT 'BEGIN{printf "%.2f", s + 100}')" >/dev/null
EID=$(sql "SELECT Id FROM Budgets WHERE UserId = '$SAMID' AND Category = 'Entertainment'")
KEY="budget:$EID:$MONTH"
check "under 75%: no alert yet" "$(sql "SELECT COUNT(*) FROM Alerts WHERE DedupeKey LIKE '$KEY:%'")" 0
withdraw $S $SCHK 80 "Cinema tickets"
check "crossing 75% -> one alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$SAMID' AND DedupeKey = '$KEY:75'")" 1
has "alert says how much is left" "$(sql "SELECT Message FROM Alerts WHERE DedupeKey = '$KEY:75'")" "left this month"
check "alert links to /budgets" "$(sql "SELECT Link FROM Alerts WHERE DedupeKey = '$KEY:75'")" "/budgets"
check "it lights up Sam's bell (unread)" "$(sql "SELECT COUNT(*) FROM Alerts WHERE DedupeKey = '$KEY:75' AND ReadAt IS NULL")" 1
withdraw $S $SCHK 1 "Movie snacks"
check "another purchase near the limit: no second 75% alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE DedupeKey = '$KEY:75'")" 1
withdraw $S $SCHK 30 "Concert tickets"
check "going over -> 100% alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$SAMID' AND DedupeKey = '$KEY:100'")" 1
withdraw $S $SCHK 5 "Movie snacks"
check "no repeat once over" "$(sql "SELECT COUNT(*) FROM Alerts WHERE DedupeKey LIKE '$KEY:%'")" 2
check "a deposit never raises a budget alert" "$(post $S /transactions/deposit /transactions/deposit -d "AccountId=$SCHK&Amount=5" --data-urlencode "Description=Cinema refund" >/dev/null; sql "SELECT COUNT(*) FROM Alerts WHERE DedupeKey LIKE '$KEY:%'")" 2
# A failed payment (insufficient funds) rolls back and must not alert.
post $S /budgets /budgets -d "Form.Category=Other" -d "Form.MonthlyLimit=1" >/dev/null
SOID=$(sql "SELECT Id FROM Budgets WHERE UserId = '$SAMID' AND Category = 'Other'")
BEFORE=$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$SAMID' AND DedupeKey LIKE 'budget:$SOID:%'")
withdraw $S $SCHK 999999 "Misc expense"
check "failed withdrawal: no budget alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$SAMID' AND DedupeKey LIKE 'budget:$SOID:%'")" "$BEFORE"

echo "== Dashboard widget"
DB=$(curl -s -b $C $B/dashboard)
has "Budgets to watch" "$DB" "Budgets to watch"
has "shows an over-budget bar" "$DB" "budget-over"
has "links to all budgets" "$DB" 'href="/budgets"'
A2=$(mktemp); demo $A2 admin >/dev/null
has "a customer with no budgets is invited to set one" "$(curl -s -b $A2 $B/dashboard)" "Set one up"

summary
