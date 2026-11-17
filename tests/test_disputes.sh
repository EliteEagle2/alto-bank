#!/usr/bin/env bash
# DISPUTES — eligibility, the form (reasons, wrong amount, duplicates, attachment, card freeze), ownership, provisional
# credit (captured at submit), approve (full / partial via the reversal system, P2P sender only) and deny (clawback that
# may overdraw, flagged), double decisions, self-decisions, simultaneous submits, the API and the seed.
# Needs B and TEST_DB (reads and "moves time" on the scratch DB).
source "$(dirname "$0")/lib.sh"; wait_up
HERE="$(cd "$(dirname "$0")" && pwd)"
T=$(cygpath -m "$(mktemp -d)" 2>/dev/null || mktemp -d)
C=$(mktemp); demo $C customer >/dev/null                     # Jordan
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
S=$(mktemp); login $S sam@altobank.com 'Demo123!' >/dev/null
A=$(mktemp); demo $A admin >/dev/null                        # Alex (admin + owner, banks here too)
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
first_id() { curl -s -b $1 $B/api/accounts | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
bal() { sql "SELECT printf('%.2f', Balance) FROM Accounts WHERE Id = $1"; }
img() { local f="$T/$1-$2.${3:-png}"; node "$HERE/make_image.js" "$f" "$1" "$2"; echo "$f"; }
last_dispute() { sql "SELECT IFNULL(MAX(Id),0) FROM Disputes"; }
# A recent, undisputed, posted withdrawal on an account (newest first, skipping n).
pick() { sql "SELECT t.Id FROM Transactions t WHERE t.AccountId = $1 AND t.Type = '${3:-Withdrawal}' AND t.Status = 'Posted' AND t.Date >= datetime('now','-50 days')
             AND CAST(t.Amount AS REAL) >= ${4:-15} AND CAST(t.Amount AS REAL) < 1000 AND NOT EXISTS (SELECT 1 FROM Disputes d WHERE d.TransactionId = t.Id)
             AND NOT EXISTS (SELECT 1 FROM Transactions r WHERE r.ReversalOfTransactionId = t.Id) ORDER BY t.Date DESC LIMIT 1 OFFSET ${2:-0}"; }
amt() { sql "SELECT printf('%.2f', Amount) FROM Transactions WHERE Id = $1"; }
# The activity API row for one transaction (asks for that transaction's own day, so it's on the first page).
txrow() { local d=$(sql "SELECT date(Date, '-1 day') FROM Transactions WHERE Id = $2") e=$(sql "SELECT date(Date, '+1 day') FROM Transactions WHERE Id = $2") q=$(sql "SELECT Description FROM Transactions WHERE Id = $2");
          curl -s -G -b $1 "$B/api/accounts/$3/transactions" -d pageSize=50 -d "from=$d" -d "to=$e" --data-urlencode "search=$q" | grep -oE "{\"id\":$2,[^}]*}"; }
# dsub <jar> <txId> <reason> <description> [extra curl -F args] -> "status redirect" (page in /tmp/post.html)
dsub() {
  local J=$1 TX=$2 R=$3 D=$4; shift 4
  local TK=$(tok $J /disputes)
  curl -s -b $J -c $J -o /tmp/post.html -w '%{http_code} %{redirect_url}' -F "Form.TransactionId=$TX" -F "Form.Reason=$R" \
    -F "Form.Description=$D" "$@" -F "__RequestVerificationToken=$TK" $B/disputes
}
adm() { post $A /admin/disputes/details/$2 /admin/disputes/$1/$2 --data-urlencode "reason=$3"; }   # adm approve|deny|startreview <id> <reason>
setprov() { sql "UPDATE BankSettings SET ProvisionalCreditOnDisputes = $1" >/dev/null; }
DESC="I never received what I paid for and the merchant won't answer me."
JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM); SID=$(uid SAM@ALTOBANK.COM)
JCHK=$(first_id $C Checking); JSAV=$(first_id $C Savings); MCHK=$(first_id $M Checking); SCHK=$(first_id $S Checking)

echo "== Seeded demo data"
check "Jordan: one refunded, one under review" "$(sql "SELECT GROUP_CONCAT(Status) FROM (SELECT Status FROM Disputes WHERE UserId = '$JID' ORDER BY Id)")" "Approved,UnderReview"
check "Maya: one denied with a provisional credit and a clawback" "$(sql "SELECT Status || ':' || (ProvisionalCreditTransactionId IS NOT NULL) || ':' || (ClawbackTransactionId IS NOT NULL) FROM Disputes WHERE UserId = '$MID'")" "Denied:1:1"
check "Jordan's refund went through the reversal system" "$(sql "SELECT r.Type || ':' || (r.ReversalOfTransactionId = d.TransactionId) FROM Disputes d JOIN Transactions r ON r.Id = d.RefundTransactionId WHERE d.UserId = '$JID' AND d.Status = 'Approved'")" "ReversalCredit:1"
check "the seeded decisions are audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('Dispute.Approve','Dispute.Deny')")" 2
check "provisional credit is OFF by default" "$(sql "SELECT ProvisionalCreditOnDisputes FROM BankSettings")" 0

echo "== Pages and navigation"
check "/disputes 200" "$(code -b $C $B/disputes)" 200
has "Accounts tab: Disputes (current)" "$(curl -s -b $C $B/disputes)" 'class="section-tab active" aria-current="page" href="/disputes"'
has "the list shows the seeded dispute" "$(curl -s -b $C $B/disputes)" "Refunded"
check "anonymous -> login" "$(code $B/disputes)" 302
check "no antiforgery token -> 400" "$(code -b $C -F Form.TransactionId=1 $B/disputes)" 400

echo "== Eligibility (decision 6: withdrawals, bill payments and P2P sends, 60 days, posted, not reversed, one dispute)"
ACT=$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?pageSize=50&from=$(date -d '-60 days' +%F 2>/dev/null || date +%F)")
has "a withdrawal can be disputed" "$ACT" '"kind":"Withdrawal"[^}]*"canDispute":true'
hasnt "a transfer between own accounts can't" "$ACT" '"kind":"TransferOut"[^}]*"canDispute":true'
hasnt "a deposit can't" "$ACT" '"kind":"Deposit"[^}]*"canDispute":true'
has "a P2P send can be disputed" "$ACT" '"kind":"PaymentSent"[^}]*"canDispute":true'
UR0=$(sql "SELECT TransactionId FROM Disputes WHERE UserId = '$JID' AND Status = 'UnderReview'")
has "the already-disputed one says Disputed (and can't be disputed again)" "$(txrow $C $UR0 $JCHK)" '"canDispute":false,"disputeStatus":"UnderReview"'
XFER=$(pick $JCHK 0 TransferOut 1)
dsub $C $XFER Other "$DESC" >/dev/null
has "own transfer refused" "$(flash $C $B/accounts)" "Transfers between your own accounts can"
DEP=$(sql "SELECT Id FROM Transactions WHERE AccountId = $JCHK AND Type = 'Deposit' ORDER BY Date DESC LIMIT 1")
dsub $C $DEP Other "$DESC" >/dev/null
has "deposit refused" "$(flash $C $B/accounts)" "Only withdrawals, bill payments, card purchases and payments you sent"
OLD=$(pick $JCHK 5)
sql "UPDATE Transactions SET Date = datetime('now','-62 days') WHERE Id = $OLD" >/dev/null   # 62, not 61: "now" is UTC and the bank day is New York, so 61 can land on day 60 in the evening
dsub $C $OLD Other "$DESC" >/dev/null
has "older than 60 days refused" "$(flash $C $B/accounts)" "within 60 days"
check "New page for an old one sends you back with the reason" "$(code -b $C "$B/disputes/new?transactionId=$OLD")" 302
UR=$(sql "SELECT TransactionId FROM Disputes WHERE UserId = '$JID' AND Status = 'UnderReview'")
dsub $C $UR Other "$DESC" >/dev/null
has "already disputed refused" "$(flash $C $B/accounts)" "already has a dispute"
URD=$(sql "SELECT Id FROM Disputes WHERE TransactionId = $UR")
check "a stale form goes to the existing dispute, never back to the form (no redirect loop)" "$(dsub $C $UR Other "$DESC")" "302 $B/disputes/$URD"
check "the form URL for it does the same" "$(curl -s -b $C -o /dev/null -w "%{redirect_url}" "$B/disputes/new?transactionId=$UR")" "$B/disputes/$URD"
REV=$(pick $JCHK 6)
post $A /admin/transactions/details/$REV /admin/transactions/reverse/$REV --data-urlencode "reason=Duplicate posting by the bank" >/dev/null
dsub $C $REV Other "$DESC" >/dev/null
has "reversed one refused" "$(flash $C $B/accounts)" "already reversed or refunded"
GOOD=$(pick $JCHK 0)
P=$(curl -s -b $C "$B/disputes/new?transactionId=$GOOD")
check "eligible -> form 200" "$(code -b $C "$B/disputes/new?transactionId=$GOOD")" 200
for r in NotAuthorized WrongAmount NotReceived Duplicate Other; do has "reason offered: $r" "$P" "value=\"$r\""; done
has "review step" "$P" "Review and submit"
check "missing transaction -> 404" "$(code -b $C "$B/disputes/new?transactionId=999999")" 404

echo "== Form validation"
dsub $C $GOOD NotReceived "Too short" >/dev/null
has "description under 20 characters" "$(cat /tmp/post.html)" "20 to 1,000 characters"
dsub $C $GOOD "" "$DESC" >/dev/null
has "no reason" "$(cat /tmp/post.html)" "Choose what went wrong"
dsub $C $GOOD WrongAmount "$DESC" >/dev/null
has "wrong amount without the expected amount" "$(cat /tmp/post.html)" "Enter the amount you expected"
dsub $C $GOOD WrongAmount "$DESC" -F "Form.ExpectedAmount=$(amt $GOOD)" >/dev/null
has "expected amount must be less than the charge" "$(cat /tmp/post.html)" "has to be less than the"
dsub $C $GOOD Duplicate "$DESC" -F "Form.DuplicateOfTransactionId=$DEP" >/dev/null
has "duplicate must point at a similar charge" "$(cat /tmp/post.html)" "Pick the original charge"
FAKE="$T/fake.jpg"; node "$HERE/make_image.js" "$FAKE" text 1
dsub $C $GOOD NotReceived "$DESC" -F "Form.Attachment=@$FAKE;type=image/jpeg" >/dev/null
has "attachment must really be JPG/PNG" "$(cat /tmp/post.html)" "must be a JPG or PNG"
check "none of those created a dispute" "$(sql "SELECT COUNT(*) FROM Disputes WHERE TransactionId = $GOOD")" 0

echo "== Ownership"
check "Maya: Jordan's dispute -> 404" "$(code -b $M $B/disputes/1)" 404
check "Maya: form for Jordan's transaction -> 404" "$(code -b $M "$B/disputes/new?transactionId=$GOOD")" 404
check "Maya: submit on Jordan's transaction -> 404" "$(dsub $M $GOOD NotReceived "$DESC" | cut -d' ' -f1)" 404
check "Maya: API for Jordan's dispute -> 404" "$(code -b $M $B/api/disputes/1)" 404

echo "== File a dispute (provisional credit OFF): approve refunds in full through the reversal system"
BAL0=$(bal $JCHK); AMT=$(amt $GOOD)
R=$(dsub $C $GOOD NotReceived "$DESC" -F "Form.Attachment=@$(img png 401);type=image/png")
D1=$(last_dispute)
check "submitted -> redirect to the dispute" "$R" "302 $B/disputes/$D1"
check "saved as Submitted, refund = the whole charge, credit off" "$(sql "SELECT Status || ':' || printf('%.2f', RefundAmount) || ':' || ProvisionalCreditEnabled FROM Disputes WHERE Id = $D1")" "Submitted:$AMT:0"
check "no money moved yet" "$(bal $JCHK)" "$BAL0"
check "submitted alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Dispute submitted' AND Link = '/disputes/$D1'")" 1
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Dispute.Submit' AND TargetId = '$D1'")" 1
check "attachment stored under a random name" "$(sql "SELECT (Attachment GLOB '[0-9a-f]*.png') FROM Disputes WHERE Id = $D1")" 1
check "owner sees the attachment" "$(code -b $C $B/disputes/$D1/attachment)" 200
check "never cached" "$(curl -s -b $C -D - -o /dev/null $B/disputes/$D1/attachment | grep -ci 'cache-control:.*no-store')" 1
check "Maya can't see it (404)" "$(code -b $M $B/disputes/$D1/attachment)" 404
check "admin can" "$(code -b $A $B/admin/disputes/attachment/$D1)" 200
has "activity badge: Disputed" "$(txrow $C $GOOD $JCHK)" '"disputeStatus":"Submitted"'
post $A /admin/transactions/details/$GOOD /admin/transactions/reverse/$GOOD --data-urlencode "reason=Trying to reverse a disputed charge" >/dev/null
has "admin can't reverse a charge with an open dispute" "$(flash $A $B/admin/transactions/details/$GOOD)" "open dispute"
check "start review" "$(adm startreview $D1 'Looking into it' | cut -d' ' -f1)" 302
check "under review + alert" "$(sql "SELECT Status FROM Disputes WHERE Id = $D1"):$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Dispute under review' AND Link = '/disputes/$D1'")" "UnderReview:1"
adm startreview $D1 'Again' >/dev/null
has "can't start review twice" "$(flash $A $B/admin/disputes/details/$D1)" "already under review"
adm approve $D1 "" >/dev/null
has "approve needs a reason" "$(flash $A $B/admin/disputes/details/$D1)" "reason of at least"
adm approve $D1 "The merchant could not show proof of delivery" >/dev/null
has "approved" "$(flash $A $B/admin/disputes/details/$D1)" "approved. \$$AMT refunded"
check "balance up by the whole charge" "$(bal $JCHK)" "$(awk -v a=$BAL0 -v b=$AMT 'BEGIN{printf "%.2f", a+b}')"
check "charge marked Reversed, linked to the reversal" "$(sql "SELECT t.Status || ':' || (t.ReversedByTransactionId = d.RefundTransactionId) || ':' || r.Type FROM Disputes d JOIN Transactions t ON t.Id = d.TransactionId JOIN Transactions r ON r.Id = d.RefundTransactionId WHERE d.Id = $D1")" "Reversed:1:ReversalCredit"
check "approved alert with the amount" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Dispute approved' AND Message LIKE '%\$$AMT%'")" 1
check "audit row" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Dispute.Approve' AND TargetId = '$D1'")" 1
has "customer timeline: Refunded + the link" "$(curl -s -b $C $B/disputes/$D1)" "See it in your activity"
has "activity badge: Refunded" "$(txrow $C $GOOD $JCHK)" '"disputeStatus":"Approved"'
adm approve $D1 "Approving again" >/dev/null
has "double approve blocked" "$(flash $A $B/admin/disputes/details/$D1)" "already refunded"
adm deny $D1 "Denying after approval" >/dev/null
has "deny after approve blocked" "$(flash $A $B/admin/disputes/details/$D1)" "already refunded"
check "still exactly one refund" "$(sql "SELECT COUNT(*) FROM Transactions WHERE ReversalOfTransactionId = $GOOD")" 1

echo "== Wrong amount: approval refunds only the difference"
WA=$(pick $JCHK 0 Withdrawal 30); WAMT=$(amt $WA); EXP=$(awk -v a=$WAMT 'BEGIN{printf "%.2f", a-12.5}')
CAT=$(sql "SELECT Category FROM Transactions WHERE Id = $WA")
SPENT0=$(curl -s -b $C $B/api/insights/summary | node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{const c=JSON.parse(s).categories.find(x=>x.key==='$CAT');console.log((c?c.amount:0).toFixed(2))})")
dsub $C $WA WrongAmount "The receipt says less than what was taken from my account." -F "Form.ExpectedAmount=$EXP" >/dev/null; D2=$(last_dispute)
check "refund amount = the difference" "$(sql "SELECT printf('%.2f', RefundAmount) FROM Disputes WHERE Id = $D2")" "12.50"
BAL1=$(bal $JCHK)
adm approve $D2 "The itemized receipt shows the lower amount" >/dev/null
check "balance up by 12.50 only" "$(awk -v a=$BAL1 -v b=$(bal $JCHK) 'BEGIN{printf "%.2f", b-a}')" "12.50"
check "partial reversal for 12.50, linked to the charge" "$(sql "SELECT r.Type || ':' || printf('%.2f', r.Amount) || ':' || r.ReversalOfTransactionId FROM Disputes d JOIN Transactions r ON r.Id = d.RefundTransactionId WHERE d.Id = $D2")" "ReversalCredit:12.50:$WA"
check "the charge itself stays Posted" "$(sql "SELECT Status FROM Transactions WHERE Id = $WA")" "Posted"
has "activity badge: Partly refunded" "$(txrow $C $WA $JCHK)" "\"disputeStatus\":\"Approved\",\"partlyRefunded\":true"
has "customer timeline links the refund transaction" "$(curl -s -b $C $B/disputes/$D2)" "Refund: <strong>Dispute refund:"
SPENT1=$(curl -s -b $C $B/api/insights/summary | node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{const c=JSON.parse(s).categories.find(x=>x.key==='$CAT');console.log((c?c.amount:0).toFixed(2))})")
WMONTH=$(sql "SELECT strftime('%Y-%m', Date) = strftime('%Y-%m','now') FROM Transactions WHERE Id = $WA")
if [ "$WMONTH" = 1 ]; then check "Insights: the partial refund comes off that category's spending" "$(awk -v a=$SPENT0 -v b=$SPENT1 'BEGIN{printf "%.2f", a-b}')" "12.50"; else ok "Insights check skipped (charge from an earlier month)"; fi
post $A /admin/transactions/details/$WA /admin/transactions/reverse/$WA --data-urlencode "reason=Trying to reverse in full" >/dev/null
has "a partly refunded charge can't then be reversed in full" "$(flash $A $B/admin/transactions/details/$WA)" "already refunded"

echo "== Duplicate charge"
DUPDESC=$(sql "SELECT Description FROM Transactions WHERE AccountId = $JCHK AND Type = 'Withdrawal' AND Status = 'Posted' AND Date >= datetime('now','-40 days') GROUP BY Description HAVING COUNT(*) >= 2 ORDER BY COUNT(*) DESC LIMIT 1")
DUP2=$(sql "SELECT t.Id FROM Transactions t WHERE t.AccountId = $JCHK AND t.Description = '$DUPDESC' AND t.Status = 'Posted' AND NOT EXISTS (SELECT 1 FROM Disputes d WHERE d.TransactionId = t.Id) ORDER BY t.Date DESC LIMIT 1")
DUP1=$(sql "SELECT Id FROM Transactions WHERE AccountId = $JCHK AND Description = '$DUPDESC' AND Id <> $DUP2 AND Status = 'Posted' AND Date >= datetime((SELECT Date FROM Transactions WHERE Id = $DUP2), '-30 days') ORDER BY Date DESC LIMIT 1")
has "form lists the similar charges" "$(curl -s -b $C "$B/disputes/new?transactionId=$DUP2")" "name=\"Form.DuplicateOfTransactionId\" value=\"$DUP1\""
check "duplicate with the original picked -> ok" "$(dsub $C $DUP2 Duplicate "$DESC" -F "Form.DuplicateOfTransactionId=$DUP1" | cut -d' ' -f1)" 302
check "original recorded" "$(sql "SELECT DuplicateOfTransactionId FROM Disputes WHERE TransactionId = $DUP2")" "$DUP1"

echo "== Deny without provisional credit"
DN=$(pick $JCHK 0); BAL2=$(bal $JCHK)
dsub $C $DN Other "$DESC" >/dev/null; D3=$(last_dispute)
adm deny $D3 "" >/dev/null
check "deny needs a reason" "$(sql "SELECT Status FROM Disputes WHERE Id = $D3")" "Submitted"
adm deny $D3 "The merchant showed it was delivered and signed for" >/dev/null
check "denied with the reason" "$(sql "SELECT Status || ':' || DecisionReason FROM Disputes WHERE Id = $D3")" "Denied:The merchant showed it was delivered and signed for"
check "no money moved" "$(bal $JCHK)" "$BAL2"
check "denied alert with the reason (as a sentence)" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Dispute denied' AND Message LIKE '%delivered and signed for.%'")" 1
has "customer sees why" "$(curl -s -b $C $B/disputes/$D3)" "Why it was denied"
adm approve $D3 "Changing my mind" >/dev/null
has "approve after deny blocked" "$(flash $A $B/admin/disputes/details/$D3)" "already denied"

echo "== Provisional credit ON: approve keeps it (no second refund)"
setprov 1
PA=$(pick $JCHK 0); PAMT=$(amt $PA); BAL3=$(bal $JCHK)
dsub $C $PA NotReceived "$DESC" >/dev/null; D4=$(last_dispute)
check "credited right away" "$(bal $JCHK)" "$(awk -v a=$BAL3 -v b=$PAMT 'BEGIN{printf "%.2f", a+b}')"
check "ProvisionalCredit row linked to the charge" "$(sql "SELECT t.Type || ':' || t.ReversalOfTransactionId FROM Disputes d JOIN Transactions t ON t.Id = d.ProvisionalCreditTransactionId WHERE d.Id = $D4")" "ProvisionalCredit:$PA"
check "provisional credit alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Provisional credit' AND Link = '/disputes/$D4'")" 1
setprov 0
adm approve $D4 "Merchant agreed to refund" >/dev/null
check "no second refund on approval" "$(bal $JCHK)" "$(awk -v a=$BAL3 -v b=$PAMT 'BEGIN{printf "%.2f", a+b}')"
check "refund = the provisional credit; charge Reversed" "$(sql "SELECT (d.RefundTransactionId = d.ProvisionalCreditTransactionId) || ':' || t.Status FROM Disputes d JOIN Transactions t ON t.Id = d.TransactionId WHERE d.Id = $D4")" "1:Reversed"
check "still only one refund-type row" "$(sql "SELECT COUNT(*) FROM Transactions WHERE ReversalOfTransactionId = $PA")" 1

echo "== The setting is captured at submit time"
CA=$(pick $JCHK 0)
dsub $C $CA Other "$DESC" >/dev/null; D5=$(last_dispute)      # filed while OFF
setprov 1
BAL4=$(bal $JCHK)
adm deny $D5 "Not enough information to go on" >/dev/null
check "filed while OFF: turning it ON later doesn't claw anything back" "$(bal $JCHK):$(sql "SELECT IFNULL(ClawbackTransactionId,'none') FROM Disputes WHERE Id = $D5")" "$BAL4:none"
setprov 0

echo "== Deny with provisional credit: clawback, and an overdraft is flagged"
setprov 1
SP=$(pick $SCHK 0 Withdrawal 20); SPAMT=$(amt $SP)
dsub $S $SP NotAuthorized "I don't recognize this purchase at all, I was at work all day." >/dev/null; D6=$(last_dispute)
setprov 0
check "credited" "$(sql "SELECT ProvisionalCreditTransactionId IS NOT NULL FROM Disputes WHERE Id = $D6")" 1
# Sam spends nearly everything (money he legitimately has), then the dispute is denied.
SBAL=$(bal $SCHK); SPEND=$(awk -v b=$SBAL 'BEGIN{printf "%.2f", b-5}')
post $S /transactions/withdraw /transactions/withdraw -d "AccountId=$SCHK&Amount=$SPEND" --data-urlencode "Description=Rent" >/dev/null
adm deny $D6 "The store has a chip and PIN receipt for this purchase" >/dev/null
check "clawback posted" "$(sql "SELECT t.Type || ':' || printf('%.2f', t.Amount) FROM Disputes d JOIN Transactions t ON t.Id = d.ClawbackTransactionId WHERE d.Id = $D6")" "ProvisionalClawback:$SPAMT"
check "account overdrawn (decision 5)" "$(sql "SELECT Balance < 0 FROM Accounts WHERE Id = $SCHK")" 1
check "flagged on the dispute" "$(sql "SELECT ClawbackOverdrew FROM Disputes WHERE Id = $D6")" 1
check "overdraft audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Dispute.ClawbackOverdraft' AND TargetId = '$SCHK'")" 1
check "customer told: denied + overdrawn" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$SID' AND Title IN ('Dispute denied','Account overdrawn')")" 2
has "admin work queue flags it" "$(curl -s -b $A $B/admin)" "is overdrawn by \$[0-9.,]* after a dispute clawback"
has "detail page flags it" "$(curl -s -b $A $B/admin/disputes/details/$D6)" "Clawback overdrew the account"
post $S /transactions/deposit /transactions/deposit -d "AccountId=$SCHK&Amount=$SPAMT" --data-urlencode "Description=Paycheck" >/dev/null

echo "== Admin Reverse after a denied dispute whose provisional credit was taken back"
MD=$(sql "SELECT TransactionId FROM Disputes WHERE UserId = '$MID' AND Status = 'Denied'")
post $A /admin/transactions/details/$MD /admin/transactions/reverse/$MD --data-urlencode "reason=Merchant refunded through us later" >/dev/null
has "allowed (the credit and clawback net to zero)" "$(flash $A $B/admin/transactions/details/$MD)" "Reversed transaction #$MD"

echo "== P2P: the sender is refunded, the recipient keeps the payment"
SENT=$(sql "SELECT t.Id FROM Transactions t WHERE t.AccountId = $JCHK AND t.Type = 'PaymentSent' AND t.Status = 'Posted' AND NOT EXISTS (SELECT 1 FROM Disputes d WHERE d.TransactionId = t.Id) ORDER BY t.Date DESC LIMIT 1")
RECV=$(sql "SELECT LinkedTransactionId FROM Transactions WHERE Id = $SENT")
RACC=$(sql "SELECT AccountId FROM Transactions WHERE Id = $RECV")
RBAL=$(bal $RACC); PSTAT=$(sql "SELECT Status FROM Payments WHERE SentTransactionId = $SENT")
dsub $C $SENT NotReceived "I paid for the tickets but they were never sent to me." >/dev/null; D7=$(last_dispute)
has "admin is told only the sender is refunded" "$(curl -s -b $A $B/admin/disputes/details/$D7)" "recipient keeps the payment"
adm approve $D7 "Refunding as a courtesy" >/dev/null
check "sender refunded" "$(sql "SELECT Status FROM Disputes WHERE Id = $D7")" "Approved"
check "recipient's balance unchanged" "$(bal $RACC)" "$RBAL"
check "recipient's leg untouched" "$(sql "SELECT Status || ':' || IFNULL(ReversedByTransactionId,'none') FROM Transactions WHERE Id = $RECV")" "Posted:none"
check "no reversal on the recipient's side" "$(sql "SELECT COUNT(*) FROM Transactions WHERE ReversalOfTransactionId = $RECV")" 0
check "the payment record is unchanged" "$(sql "SELECT Status FROM Payments WHERE SentTransactionId = $SENT")" "$PSTAT"
check "the refund is from Alto (described as a dispute refund, no counterparty)" "$(sql "SELECT (Description LIKE 'Dispute refund:%') || ':' || IFNULL(CounterpartyAccountId,'none') FROM Transactions WHERE Id = (SELECT RefundTransactionId FROM Disputes WHERE Id = $D7)")" "1:none"

echo "== I didn't authorize it: freeze the card in one click"
CARD=$(sql "SELECT Id FROM Cards WHERE AccountId = $JCHK AND Status = 'Active' LIMIT 1")
NA=$(pick $JCHK 0)
R=$(dsub $C $NA NotAuthorized "There is a charge here that I definitely did not make." -F Form.FreezeCard=true)
check "submitted" "$(echo $R | cut -d' ' -f1)" 302
check "card frozen" "$(sql "SELECT Status FROM Cards WHERE Id = $CARD")" "Frozen"
has "told about the freeze" "$(flash $C $B/disputes/$(last_dispute))" "debit card"
sql "UPDATE Cards SET Status = 'Active' WHERE Id = $CARD" >/dev/null

echo "== Staff can't decide their own dispute"
ACHK=$(first_id $A Checking); OWN=$(pick $ACHK 0 Withdrawal 1)
dsub $A $OWN NotReceived "$DESC" >/dev/null; D8=$(last_dispute)
check "Alex filed one on his own account" "$(sql "SELECT AccountId FROM Disputes WHERE Id = $D8")" "$ACHK"
adm approve $D8 "Approving my own" >/dev/null
has "blocked" "$(flash $A $B/admin/disputes/details/$D8)" "on your own profile or accounts"
check "still submitted" "$(sql "SELECT Status FROM Disputes WHERE Id = $D8")" "Submitted"

echo "== Simultaneous submissions on the same transaction create only one dispute"
SIM=$(pick $JCHK 0)
for n in 1 2 3 4; do ( dsub $C $SIM NotReceived "$DESC" > "$T/sim$n.txt" ) & done; wait
check "exactly one dispute" "$(sql "SELECT COUNT(*) FROM Disputes WHERE TransactionId = $SIM")" 1
check "every request ends on that one dispute (the losers are shown it, not an error)" "$(for f in $T/sim*.txt; do cat "$f"; echo; done | sort -u | wc -l | tr -d ' ') $(for f in $T/sim*.txt; do cat "$f"; echo; done | sort -u)" "1 302 $B/disputes/$(sql "SELECT Id FROM Disputes WHERE TransactionId = $SIM")"

echo "== API"
L=$(curl -s -b $C $B/api/disputes)
has "list" "$L" "\"id\":$D1,"
hasnt "no internal file names" "$L" '"attachment":"[0-9a-f]\{32\}'
check "GET one" "$(code -b $C $B/api/disputes/$D1)" 200
check "anonymous -> 401" "$(code $B/api/disputes)" 401
APX=$(pick $JCHK 0)
check "POST without antiforgery token -> 400" "$(curl -s -o /dev/null -w '%{http_code}' -b $C -F transactionId=$APX -F reason=Other -F "description=$DESC" $B/api/disputes)" 400
TK=$(tok $C /disputes)
R=$(curl -s -b $C -w '\n%{http_code}' -H "RequestVerificationToken: $TK" -F transactionId=$APX -F reason=NotReceived -F "description=$DESC" -F "attachment=@$(img png 402);type=image/png" $B/api/disputes)
check "POST multipart with the token -> 201" "$(echo "$R" | tail -1)" 201
has "returns the dispute" "$(echo "$R" | head -1)" '"status":"Submitted"'
R=$(curl -s -b $C -w '\n%{http_code}' -H "RequestVerificationToken: $TK" -F transactionId=$APX -F reason=Other -F "description=$DESC" $B/api/disputes)
check "API: second dispute on the same transaction -> 400" "$(echo "$R" | tail -1)" 400
R=$(curl -s -b $M -w '\n%{http_code}' -H "RequestVerificationToken: $(tok $M /disputes)" -F transactionId=$(pick $JCHK 0) -F reason=Other -F "description=$DESC" $B/api/disputes)
check "API: another customer's transaction -> 404" "$(echo "$R" | tail -1)" 404
R=$(curl -s -b $C -w '\n%{http_code}' -H "RequestVerificationToken: $TK" -F transactionId=$(pick $JCHK 0) -F reason=Bogus -F "description=$DESC" $B/api/disputes)
check "API: unknown reason -> 400" "$(echo "$R" | tail -1)" 400

echo "== Admin queue"
Q=$(curl -s -b $A $B/admin/disputes)
check "queue 200" "$(code -b $A $B/admin/disputes)" 200
has "opens on Submitted" "$Q" 'nav-link active[^>]*>Submitted'
has "admin nav: Disputes" "$Q" '<span>Disputes</span>'
has "oldest first" "$(curl -s -b $A "$B/admin/disputes?status=all" | grep -oE 'DSP-[0-9]{6}' | head -2 | tr '\n' ' ')" "DSP-000001 DSP-000002"
has "overview work queue: Disputes filter" "$(curl -s -b $A $B/admin)" 'data-queue-filter="dispute"'
check "customers can't open the queue" "$(code -b $C $B/admin/disputes)" 302
has "settings toggle" "$(curl -s -b $A $B/admin/settings)" "Provisional credit on new disputes"

echo "== Nothing deleted"
check "every dispute still points at its transaction" "$(sql "SELECT COUNT(*) FROM Disputes d LEFT JOIN Transactions t ON t.Id = d.TransactionId WHERE t.Id IS NULL")" 0

summary
