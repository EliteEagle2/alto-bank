#!/usr/bin/env bash
# MOBILE CHECK DEPOSIT — uploads (type by signature, size, duplicates), limits, pending vs available balance, ownership
# of deposits and images, the admin review queue (approve / reject, audit rows, decided once, frozen accounts), alerts,
# the API and the seeded demo data. Needs B and TEST_DB (reads the scratch DB).
source "$(dirname "$0")/lib.sh"; wait_up
HERE="$(cd "$(dirname "$0")" && pwd)"
T=$(cygpath -m "$(mktemp -d)" 2>/dev/null || mktemp -d)   # Windows-style path: curl and node both understand it
C=$(mktemp); demo $C customer >/dev/null                     # Jordan
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
S=$(mktemp); login $S sam@altobank.com 'Demo123!' >/dev/null
A=$(mktemp); demo $A admin >/dev/null                        # Alex (admin + owner, also banks here)
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
first_id() { curl -s -b $1 $B/api/accounts | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
bal() { awk -v a="$(curl -s -b $1 $B/api/accounts/$2 | grep -oE '"balance":[0-9.-]+' | cut -d: -f2)" 'BEGIN{printf "%.2f", a}'; }
img() { local f="$T/$1-$2.${3:-png}"; node "$HERE/make_image.js" "$f" "$1" "$2"; echo "$f"; }
last_id() { sql "SELECT IFNULL(MAX(Id),0) FROM CheckDeposits"; }
# deposit <jar> <account> <amount> <front> <back> [extra curl args] -> "status redirect"; page saved to /tmp/post.html
deposit() {
  local J=$1 ACC=$2 AMT=$3 F=$4 BK=$5; shift 5
  local TK=$(tok $J /checks)
  curl -s -b $J -c $J -o /tmp/post.html -w '%{http_code} %{redirect_url}' \
    -F "Form.AccountId=$ACC" -F "Form.Amount=$AMT" -F "Form.Front=@$F;type=image/jpeg;filename=IMG_0001.jpg" -F "Form.Back=@$BK;type=image/jpeg;filename=IMG_0002.jpg" \
    -F Form.Signed=true -F Form.Restricted=true -F Form.InFrame=true "$@" -F "__RequestVerificationToken=$TK" $B/checks
}
adm() { post $A /admin/checkdeposits/details/$2 /admin/checkdeposits/$1/$2 --data-urlencode "reason=$3"; }   # adm approve|reject <id> <reason>
JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM); SID=$(uid SAM@ALTOBANK.COM)
JCHK=$(first_id $C Checking); JSAV=$(first_id $C Savings); MCHK=$(first_id $M Checking); SCHK=$(first_id $S Checking)

echo "== Seeded demo data"
check "Jordan: 1 approved + 1 pending" "$(sql "SELECT GROUP_CONCAT(Status) FROM (SELECT Status FROM CheckDeposits WHERE UserId = '$JID' ORDER BY Id)")" "Approved,Pending"
check "Maya: 1 rejected (back not endorsed)" "$(sql "SELECT Status || ':' || (RejectReason LIKE '%endorsed%') FROM CheckDeposits WHERE UserId = '$MID'")" "Rejected:1"
check "Samuel: none" "$(sql "SELECT COUNT(*) FROM CheckDeposits WHERE UserId = '$SID'")" 0
check "the approved one posted a CheckDeposit transaction" "$(sql "SELECT t.Type FROM CheckDeposits d JOIN Transactions t ON t.Id = d.TransactionId WHERE d.UserId = '$JID' AND d.Status = 'Approved'")" "CheckDeposit"
check "stored names are random, not the uploader's" "$(sql "SELECT COUNT(*) FROM CheckDeposits WHERE FrontImage NOT GLOB '[0-9a-f]*.png' OR length(FrontImage) <> 36")" 0
check "seeded image is a real PNG" "$(curl -s -b $C $B/checks/1/image/front | head -c 4 | od -An -tx1 | tr -d ' \n')" "89504e47"

echo "== Pages and navigation"
P=$(curl -s -b $C $B/checks)
check "/checks 200" "$(code -b $C $B/checks)" 200
has "Move money tab: Deposit a check" "$P" 'class="section-tab active"[^>]*href="/checks"\|href="/checks"[^>]*aria-current="page"'
has "camera capture on phones" "$P" 'accept="image/\*" capture="environment"'
has "endorsement checklist" "$P" 'For mobile deposit only at Alto Bank'
has "shows the limits" "$P" 'Left today'
has "Deposit page offers a check deposit" "$(curl -s -b $C $B/transactions/deposit)" 'Have a paper check?'
check "anonymous -> login" "$(code $B/checks)" 302
check "no antiforgery token -> 400" "$(code -b $C -F Form.Amount=5 $B/checks)" 400

echo "== A good deposit (pending, not in the balance)"
BAL0=$(bal $C $JCHK)
F1=$(img png 101); B1=$(img png 102)
BEFORE=$(last_id)
R=$(deposit $C $JCHK 125.50 "$F1" "$B1")
NEW=$(last_id)
check "deposit -> redirect to its status page" "$R" "302 $B/checks/$NEW"
check "saved as Pending" "$(sql "SELECT Status || ':' || Amount || ':' || AccountId FROM CheckDeposits WHERE Id = $NEW")" "Pending:125.5:$JCHK"
check "original file name never stored" "$(sql "SELECT COUNT(*) FROM CheckDeposits WHERE Id = $NEW AND (FrontImage LIKE '%IMG_%' OR BackImage LIKE '%IMG_%')")" 0
check "stored under a random .png name (real type, not the .jpg it was called)" "$(sql "SELECT (FrontImage GLOB '[0-9a-f]*.png') FROM CheckDeposits WHERE Id = $NEW")" 1
check "front hash saved (SHA-256 hex)" "$(sql "SELECT length(FrontHash) FROM CheckDeposits WHERE Id = $NEW")" 64
has "status page: In review" "$(curl -s -b $C $B/checks/$NEW)" "In review"
check "posted balance unchanged" "$(bal $C $JCHK)" "$BAL0"
D=$(curl -s -b $C $B/accounts/$JCHK)
has "account header: Pending line" "$D" 'acct-label">Pending'
has "activity: Pending - mobile deposit" "$D" "Pending - mobile deposit"
AVAIL=$(printf '%s' "$D" | tr -s ' \r\n' ' ' | grep -oE 'Available balance</div> ?<div class="acct-available"><span class="money">\$[0-9,.]+' | grep -oE '[0-9,.]+$' | tr -d ,)
check "available balance is still the posted balance (pending not included)" "$AVAIL" "$BAL0"
check "received alert" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Type = 'CheckReceived' AND Link = '/checks/$NEW'")" 1
check "audited" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'CheckDeposit.Submit' AND TargetId = '$NEW'")" 1

echo "== Files that must be refused"
FT="$T/not-a-photo.jpg"; node "$HERE/make_image.js" "$FT" text 1
deposit $C $JCHK 20 "$FT" "$(img png 103)" >/dev/null
has "text file renamed .jpg -> refused" "$(cat /tmp/post.html)" "must be a JPG or PNG photo"
GIF="$T/anim.jpg"; node "$HERE/make_image.js" "$GIF" gif 1
deposit $C $JCHK 20 "$(img png 104)" "$GIF" >/dev/null
has "GIF renamed .jpg (back) -> refused" "$(cat /tmp/post.html)" "back of the check must be a JPG or PNG"
BIG=$(img big 1)
deposit $C $JCHK 20 "$BIG" "$(img png 105)" >/dev/null
has "over 5 MB -> refused" "$(cat /tmp/post.html)" "up to 5 MB"
JPG=$(img jpg 7)
R=$(deposit $C $JCHK 20.25 "$JPG" "$(img png 106)")
check "a JPG (by signature) is accepted" "$(echo $R | cut -d' ' -f1)" 302
SAME=$(img png 107)
deposit $C $JCHK 20 "$SAME" "$SAME" >/dev/null
has "front and back the same picture -> refused" "$(cat /tmp/post.html)" "same picture"
TK=$(tok $C /checks)
curl -s -b $C -o /tmp/post.html -F "Form.AccountId=$JCHK" -F Form.Amount=20 -F "Form.Front=@$(img png 108);type=image/png" -F Form.Signed=true -F Form.Restricted=true -F Form.InFrame=true -F "__RequestVerificationToken=$TK" $B/checks
has "missing back photo -> refused" "$(cat /tmp/post.html)" "Add a photo of the back of the check"
TK=$(tok $C /checks)   # the endorsement boxes NOT ticked (no Form.Signed / Restricted / InFrame fields)
curl -s -b $C -o /tmp/post.html -F "Form.AccountId=$JCHK" -F Form.Amount=20 -F "Form.Front=@$(img png 109);type=image/png" -F "Form.Back=@$(img png 110);type=image/png" -F "__RequestVerificationToken=$TK" $B/checks
has "checklist not ticked -> refused" "$(cat /tmp/post.html)" "Sign the back of the check first"
TK=$(tok $C /checks)
curl -s -b $C -o /tmp/post.html -F "Form.AccountId=$JCHK" -F Form.Amount=20 -F "Form.Front=@$FT;type=image/jpeg" -F "Form.Back=@$(img png 117);type=image/png" -F "__RequestVerificationToken=$TK" $B/checks
P2=$(cat /tmp/post.html)
has "unticked checklist AND a bad photo: both reported at once (checklist)" "$P2" "Sign the back of the check first"
has "unticked checklist AND a bad photo: both reported at once (photo)" "$P2" "must be a JPG or PNG photo"
check "none of those created a deposit" "$(sql "SELECT COUNT(*) FROM CheckDeposits WHERE Id > $NEW AND Amount = 20")" 0

echo "== Duplicate guard"
deposit $C $JCHK 125.50 "$F1" "$(img png 111)" >/dev/null
has "same front image again -> refused (own)" "$(cat /tmp/post.html)" "You already deposited this check on"
deposit $M $MCHK 125.50 "$F1" "$(img png 112)" >/dev/null
has "another customer with the same image -> refused, no details" "$(cat /tmp/post.html)" "This check has already been deposited."
hasnt "Maya isn't told whose it was" "$(cat /tmp/post.html)" "Jordan"

echo "== Accounts"
deposit $C $MCHK 30 "$(img png 113)" "$(img png 114)" >/dev/null
has "into Maya's account -> refused" "$(cat /tmp/post.html)" "Choose one of your accounts."
sql "UPDATE Accounts SET Status = 'Frozen' WHERE Id = $JSAV" >/dev/null
deposit $C $JSAV 30 "$(img png 115)" "$(img png 116)" >/dev/null
has "into a frozen account -> refused" "$(cat /tmp/post.html)" "is frozen"
sql "UPDATE Accounts SET Status = 'Active' WHERE Id = $JSAV" >/dev/null

echo "== Limits (per check, per bank day; pending + approved count, rejected doesn't)"
deposit $S $SCHK 5000.01 "$(img png 120)" "$(img png 121)" >/dev/null
has "over the per-check limit -> refused" "$(cat /tmp/post.html)" "limited to \$5,000.00 per check"
sql "UPDATE BankSettings SET MobileDepositDailyLimit = 300" >/dev/null
check "200 ok" "$(deposit $S $SCHK 200 "$(img png 122)" "$(img png 123)" | cut -d' ' -f1)" 302
deposit $S $SCHK 150 "$(img png 124)" "$(img png 125)" >/dev/null
has "150 more -> over the daily limit, says what's left" "$(cat /tmp/post.html)" "up to \$100.00 more today"
check "exactly 100 -> ok" "$(deposit $S $SCHK 100 "$(img png 126)" "$(img png 127)" | cut -d' ' -f1)" 302
SLAST=$(last_id)
deposit $S $SCHK 1 "$(img png 128)" "$(img png 129)" >/dev/null
has "limit reached" "$(cat /tmp/post.html)" "reached today"
check "reject the 100 (as admin)" "$(adm reject $SLAST 'Amount is unreadable' | cut -d' ' -f1)" 302
check "a rejected check frees up the limit" "$(deposit $S $SCHK 50 "$(img png 130)" "$(img png 131)" | cut -d' ' -f1)" 302
sql "UPDATE BankSettings SET MobileDepositDailyLimit = 10000" >/dev/null

echo "== Ownership: deposits and images"
check "Maya can't see Jordan's deposit (404)" "$(code -b $M $B/checks/$NEW)" 404
check "Maya can't see Jordan's front image (404)" "$(code -b $M $B/checks/$NEW/image/front)" 404
check "Maya can't see Jordan's back image (404)" "$(code -b $M $B/checks/$NEW/image/back)" 404
check "unknown side -> 404" "$(code -b $C $B/checks/$NEW/image/sideways)" 404
check "Jordan sees his own image" "$(code -b $C $B/checks/$NEW/image/front)" 200
check "image never cached" "$(curl -s -b $C -D - -o /dev/null $B/checks/$NEW/image/front | grep -ci 'cache-control:.*no-store')" 1
check "anonymous image -> login" "$(code $B/checks/$NEW/image/front)" 302
check "admin sees any image" "$(code -b $A "$B/admin/checkdeposits/image/$NEW?side=front")" 200
check "customer can't use the admin image URL" "$(code -b $C "$B/admin/checkdeposits/image/$NEW?side=front")" 302
sql "UPDATE CheckDeposits SET BackImage = '../../appsettings.json' WHERE Id = $NEW" >/dev/null
check "a stored name that isn't ours is never opened (path trick -> 404)" "$(code -b $A "$B/admin/checkdeposits/image/$NEW?side=back")" 404
sql "UPDATE CheckDeposits SET BackImage = FrontImage WHERE Id = $NEW" >/dev/null
check "no images on disk are web-reachable (wwwroot)" "$(code $B/App_Data/uploads/$(sql "SELECT FrontImage FROM CheckDeposits WHERE Id = $NEW"))" 404

echo "== API"
L=$(curl -s -b $C $B/api/checkdeposits)
has "list has the new deposit" "$L" "\"id\":$NEW,"
hasnt "no file names or hashes in the API" "$L" 'FrontHash\|frontHash\|frontImage"'
check "GET one" "$(code -b $C $B/api/checkdeposits/$NEW)" 200
check "GET Maya's -> 404" "$(code -b $C $B/api/checkdeposits/$(sql "SELECT Id FROM CheckDeposits WHERE UserId = '$MID' LIMIT 1"))" 404
check "anonymous -> 401" "$(code $B/api/checkdeposits)" 401
check "POST without antiforgery token -> 400" "$(curl -s -o /dev/null -w '%{http_code}' -b $C -F accountId=$JCHK -F amount=12 -F "front=@$(img png 140);type=image/png" -F "back=@$(img png 141);type=image/png" $B/api/checkdeposits)" 400
TK=$(tok $C /checks)
R=$(curl -s -b $C -w '\n%{http_code}' -H "RequestVerificationToken: $TK" -F accountId=$JCHK -F amount=12.34 -F endorsed=true -F "front=@$(img png 142);type=image/png" -F "back=@$(img png 143);type=image/png" $B/api/checkdeposits)
check "POST multipart with the token -> 201" "$(echo "$R" | tail -1)" 201
has "returns the pending deposit" "$(echo "$R" | head -1)" '"amount":12.34,"status":"Pending"'
R=$(curl -s -b $C -w '\n%{http_code}' -H "RequestVerificationToken: $TK" -F accountId=$JCHK -F amount=12 -F endorsed=true -F "front=@$FT;type=image/jpeg" -F "back=@$(img png 144);type=image/png" $B/api/checkdeposits)
check "API: fake JPG -> 400" "$(echo "$R" | tail -1)" 400
has "API says why" "$(echo "$R" | head -1)" "JPG or PNG"
R=$(curl -s -b $C -w '\n%{http_code}' -H "RequestVerificationToken: $TK" -F accountId=$JCHK -F amount=12 -F "front=@$(img png 145);type=image/png" -F "back=@$(img png 146);type=image/png" $B/api/checkdeposits)
check "API: not endorsed -> 400" "$(echo "$R" | tail -1)" 400
has "API says to endorse" "$(echo "$R" | head -1)" "Endorse the back"

echo "== Admin queue"
Q=$(curl -s -b $A $B/admin/checkdeposits)
check "queue 200" "$(code -b $A $B/admin/checkdeposits)" 200
has "opens on Pending" "$Q" 'nav-link active[^>]*>Pending'
has "lists Jordan's new check" "$Q" "CHK-$(printf '%06d' $NEW)"
has "admin nav: Check Deposits" "$Q" '<span>Check Deposits</span>'
has "All tab" "$(curl -s -b $A "$B/admin/checkdeposits?status=all")" 'nav-link active[^>]*>All'
has "overview work queue: Checks filter" "$(curl -s -b $A $B/admin)" 'data-queue-filter="check"'
DT=$(curl -s -b $A $B/admin/checkdeposits/details/$NEW)
has "detail: both images" "$DT" "image/$NEW?side=back"
has "detail: click to enlarge" "$DT" "Click to enlarge"
has "detail: customer's other deposits" "$DT" "other check deposits"
check "customers can't open the queue" "$(code -b $C $B/admin/checkdeposits)" 302

echo "== Approve"
BAL1=$(bal $C $JCHK)
R=$(adm approve $NEW "")
has "reason required" "$(flash $A $B/admin/checkdeposits/details/$NEW)" "reason of at least"
check "still pending" "$(sql "SELECT Status FROM CheckDeposits WHERE Id = $NEW")" "Pending"
adm approve $NEW "Image clear, amount matches" >/dev/null
has "approved" "$(flash $A $B/admin/checkdeposits/details/$NEW)" "approved. \$125.50 posted"
check "status Approved + reviewer" "$(sql "SELECT Status || ':' || ReviewerEmail FROM CheckDeposits WHERE Id = $NEW")" "Approved:admin@altobank.com"
check "balance went up by exactly 125.50" "$(awk -v a=$BAL1 -v b=$(bal $C $JCHK) 'BEGIN{printf "%.2f", b-a}')" "125.50"
check "posted as a CheckDeposit transaction (Income)" "$(sql "SELECT t.Type || ':' || t.Amount || ':' || t.Category FROM CheckDeposits d JOIN Transactions t ON t.Id = d.TransactionId WHERE d.Id = $NEW")" "CheckDeposit:125.5:Income"
check "audit row with the reason" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'CheckDeposit.Approve' AND TargetId = '$NEW' AND Reason = 'Image clear, amount matches'")" 1
check "approved alert with the amount" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Type = 'CheckApproved' AND Message LIKE '%\$125.50%'")" 1
hasnt "no longer pending on the account" "$(curl -s -b $C $B/accounts/$JCHK)" "\$125.50</span></span>"
has "status page: Deposited" "$(curl -s -b $C $B/checks/$NEW)" "Deposited"
adm approve $NEW "Approving again" >/dev/null
has "double approve blocked" "$(flash $A $B/admin/checkdeposits/details/$NEW)" "already approved"
check "still one transaction" "$(sql "SELECT COUNT(*) FROM Transactions WHERE Type = 'CheckDeposit' AND Memo = 'CHK-$(printf '%06d' $NEW)'")" 1
adm reject $NEW "Rejecting after approval" >/dev/null
has "can't reject an approved check" "$(flash $A $B/admin/checkdeposits/details/$NEW)" "already approved"

echo "== Reject"
F2=$(img png 150); deposit $C $JCHK 75 "$F2" "$(img png 151)" >/dev/null; RJ=$(last_id)
adm reject $RJ "" >/dev/null
check "reason required" "$(sql "SELECT Status FROM CheckDeposits WHERE Id = $RJ")" "Pending"
adm reject $RJ "The back is not endorsed" >/dev/null
check "rejected with the reason" "$(sql "SELECT Status || ':' || RejectReason FROM CheckDeposits WHERE Id = $RJ")" "Rejected:The back is not endorsed"
check "audit row" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'CheckDeposit.Reject' AND TargetId = '$RJ'")" 1
check "rejected alert with the reason (as a sentence)" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Type = 'CheckRejected' AND Message LIKE '%The back is not endorsed. No money was added.%'")" 1
SP=$(curl -s -b $C $B/checks/$RJ)
has "customer sees: Not accepted" "$SP" "Not accepted"
has "customer sees the reason" "$SP" "The back is not endorsed"
check "no money moved" "$(sql "SELECT IFNULL(TransactionId, 'none') FROM CheckDeposits WHERE Id = $RJ")" "none"
adm approve $RJ "Approve after reject" >/dev/null
has "can't approve a rejected check" "$(flash $A $B/admin/checkdeposits/details/$RJ)" "already rejected"
R=$(deposit $C $JCHK 75 "$F2" "$(img png 152)")
check "a rejected image may be deposited again" "$(echo $R | cut -d' ' -f1)" 302

echo "== Frozen account at approval time"
deposit $C $JSAV 60 "$(img png 160)" "$(img png 161)" >/dev/null; FZ=$(last_id)
SAVBAL=$(bal $C $JSAV)
sql "UPDATE Accounts SET Status = 'Frozen' WHERE Id = $JSAV" >/dev/null
has "detail explains why it can't be approved" "$(curl -s -b $A $B/admin/checkdeposits/details/$FZ)" "approve right now"
adm approve $FZ "Trying while frozen" >/dev/null
has "approve refused: frozen" "$(flash $A $B/admin/checkdeposits/details/$FZ)" "is frozen"
check "still pending" "$(sql "SELECT Status FROM CheckDeposits WHERE Id = $FZ")" "Pending"
check "no money moved" "$(sql "SELECT Balance FROM Accounts WHERE Id = $JSAV" | awk '{printf "%.2f", $1}')" "$SAVBAL"
sql "UPDATE Accounts SET Status = 'Closed' WHERE Id = $JSAV" >/dev/null
adm approve $FZ "Trying while closed" >/dev/null
has "approve refused: closed" "$(flash $A $B/admin/checkdeposits/details/$FZ)" "is closed"
sql "UPDATE Accounts SET Status = 'Active' WHERE Id = $JSAV" >/dev/null

echo "== Staff can't approve their own check"
ACHK=$(first_id $A Checking)
deposit $A $ACHK 40 "$(img png 170)" "$(img png 171)" >/dev/null; OWN=$(last_id)
check "Alex's own deposit is pending" "$(sql "SELECT Status FROM CheckDeposits WHERE Id = $OWN AND AccountId = $ACHK")" "Pending"
adm approve $OWN "Approving my own" >/dev/null
has "blocked: own records" "$(flash $A $B/admin/checkdeposits/details/$OWN)" "on your own profile or accounts"
check "still pending" "$(sql "SELECT Status FROM CheckDeposits WHERE Id = $OWN")" "Pending"

echo "== Admin settings: the limits"
SETT() { post $A /admin/settings /admin/settings -d SavingsApyPercent=4.1 -d CheckingApyPercent=0 -d DefaultDailyTransferLimit=5000 -d LargeTransactionThreshold=2500 -d AutoApproveApplications=true -d "MobileDepositPerCheckLimit=$1" -d "MobileDepositDailyLimit=$2" --data-urlencode "Reason=Testing deposit limits"; }
SETT 6000 4000 >/dev/null
has "per-check over daily refused" "$(flash $A $B/admin/settings)" "more than the daily limit"
SETT 4000 9000 >/dev/null
check "saved" "$(sql "SELECT MobileDepositPerCheckLimit || ':' || MobileDepositDailyLimit FROM BankSettings")" "4000:9000"
deposit $S $SCHK 4500 "$(img png 180)" "$(img png 181)" >/dev/null
has "new per-check limit applies" "$(cat /tmp/post.html)" "limited to \$4,000.00 per check"
SETT 5000 10000 >/dev/null
check "restored" "$(sql "SELECT MobileDepositPerCheckLimit || ':' || MobileDepositDailyLimit FROM BankSettings")" "5000:10000"

echo "== Two submits at the same moment (daily limit and duplicate guard can't be raced)"
sql "UPDATE BankSettings SET MobileDepositDailyLimit = 99999" >/dev/null
SAMUSED=$(sql "SELECT IFNULL(SUM(Amount),0) FROM CheckDeposits WHERE UserId = '$SID' AND Status <> 'Rejected'")
sql "UPDATE BankSettings SET MobileDepositDailyLimit = $(awk -v u=$SAMUSED 'BEGIN{printf "%.2f", u + 500}')" >/dev/null   # room for exactly one more $400
for n in 1 2 3 4; do F="$(img png $((300+n)))"; BK="$(img png $((310+n)))"; ( SJ=$(mktemp); cp $S $SJ; deposit $SJ $SCHK 400 "$F" "$BK" > "$T/race$n.txt" ) & done; wait   # own cookie jar each
check "4 parallel \$400 deposits with \$500 left: exactly one accepted" "$(for f in $T/race*.txt; do cat "$f"; echo; done | grep -cE '^302 [^ ]*/checks/[0-9]+$')" 1
sql "UPDATE BankSettings SET MobileDepositDailyLimit = 10000" >/dev/null
DUP=$(img png 320)
for n in 1 2 3; do ( CJ=$(mktemp); cp $C $CJ; deposit $CJ $JCHK 33 "$DUP" "$(img png $((330+n)))" > "$T/dup$n.txt" ) & done; wait   # own cookie jar each
check "3 parallel deposits of the same image: exactly one accepted" "$(for f in $T/dup*.txt; do cat "$f"; echo; done | grep -cE '^302 [^ ]*/checks/[0-9]+$')" 1

echo "== Approve refuses a check image that was already deposited"
ORIG=$(sql "SELECT Id FROM CheckDeposits WHERE UserId = '$JID' AND Status = 'Approved' ORDER BY Id LIMIT 1")
# A copy slipped in some other way (e.g. before the guard existed): same front hash, still pending.
sql "INSERT INTO CheckDeposits (UserId, AccountId, Amount, FrontImage, BackImage, FrontHash, Status, SubmittedAt) SELECT UserId, AccountId, Amount, FrontImage, BackImage, FrontHash, 'Pending', SubmittedAt FROM CheckDeposits WHERE Id = $ORIG" >/dev/null
COPY=$(last_id)
adm approve $COPY "Looks fine to me" >/dev/null
has "refused: already deposited" "$(flash $A $B/admin/checkdeposits/details/$COPY)" "already deposited as CHK-$(printf '%06d' $ORIG)"
check "still pending" "$(sql "SELECT Status FROM CheckDeposits WHERE Id = $COPY")" "Pending"
has "detail warns about the duplicate image" "$(curl -s -b $A $B/admin/checkdeposits/details/$COPY)" "This front image was submitted before"

echo "== Nothing deleted"
check "every deposit still has both files on record" "$(sql "SELECT COUNT(*) FROM CheckDeposits WHERE FrontImage = '' OR BackImage = ''")" 0

summary
