#!/usr/bin/env bash
# Regression tests for the Part 2 review findings.
source "$(dirname "$0")/lib.sh"; wait_up
A=$(mktemp); demo $A admin >/dev/null
flash() { curl -s -b $A -c $A "$1" | tr -s ' \r\n' ' ' | grep -oE 'alert-alto-(success|error)[^>]*> *<i[^>]*></i>[^<]+' | sed 's/.*<\/i>//' | head -1; }

# Submit a modal EXACTLY like a browser: use the token rendered INSIDE that modal's own <form>.
modal_post() { # page, modal id, extra curl args...
  local page="$1" id="$2"; shift 2
  local html=$(curl -s -b $A -c $A "$B$page" | tr -s ' \r\n' ' ')
  local form=$(echo "$html" | grep -oE "id=\"$id\".{0,3000}" | grep -oE '<form class="modal-content"[^>]*>.*' | sed 's#</form>.*##')
  local action=$(echo "$form" | grep -oE 'action="[^"]+"' | head -1 | cut -d'"' -f2 | sed 's/&amp;/\&/g')
  local token=$(echo "$form" | grep -oE 'name="__RequestVerificationToken" type="hidden" value="[^"]+"' | sed 's/.*value="//;s/"$//')
  [ -z "$token" ] && { echo "NO-TOKEN"; return; }
  curl -s -b $A -c $A -o /dev/null -w '%{http_code}' "$@" --data-urlencode "__RequestVerificationToken=$token" "$B$action"
}

echo "== 1. Confirm modals carry their own anti-forgery token"
for page in /admin/accounts/details/1 /admin/accounts/details/3 /admin/settings /admin/transactions/details/5; do
  html=$(curl -s -b $A "$B$page" | tr -s ' \r\n' ' ' | sed 's#</form>#</form>\n#g')   # one form per line
  forms=$(echo "$html" | grep -c 'class="modal-content"')
  withtok=$(echo "$html" | grep 'class="modal-content"' | grep -c '__RequestVerificationToken')
  check "$page: all $forms modal forms carry their own token" "$withtok" "$forms"
done
check "freeze via the modal's own form (browser-equivalent)" "$(modal_post /admin/accounts/details/1 freezeModal --data-urlencode 'reason=Modal token test')" 302
has "  ...account is frozen" "$(curl -s -b $A $B/admin/accounts/details/1)" "status-frozen"
check "unfreeze via the modal's own form" "$(modal_post /admin/accounts/details/1 unfreezeModal --data-urlencode 'reason=Modal token test done')" 302

echo "== 1b. Admin links + redirects stay inside /admin (customer route accounts/{id} must not win)"
for page in /admin /admin/accounts /admin/transactions /admin/transactions/details/5 "/admin/customers/details/$(curl -s -b $A "$B/admin/customers?search=jordan" | grep -oE 'customers/details/[0-9a-f-]+' | head -1 | cut -d/ -f3)"; do
  hasnt "$page has no links to customer /accounts/ pages" "$(curl -s -b $A "$B$page")" 'href="/accounts/'
done
has "account list links to admin account page" "$(curl -s -b $A $B/admin/accounts)" 'href="/admin/accounts/details/'
T=$(tok $A /admin/accounts/details/1)
check "redirect after an account action stays in /admin" "$(curl -s -b $A -c $A -o /dev/null -w '%{redirect_url}' --data-urlencode 'reason=Redirect target test' --data-urlencode "__RequestVerificationToken=$T" $B/admin/accounts/freeze/1)" "$B/admin/accounts/details/1"
T=$(tok $A /admin/accounts/details/1)
curl -s -b $A -c $A -o /dev/null --data-urlencode 'reason=Redirect target test done' --data-urlencode "__RequestVerificationToken=$T" $B/admin/accounts/unfreeze/1

echo "== 2. Locking a customer ends their session immediately"
C=$(mktemp); login $C sam@altobank.com 'Demo123!' >/dev/null
check "Sam is signed in" "$(code -b $C $B/dashboard)" 200
SAMID=$(curl -s -b $A "$B/admin/customers?search=sam" | grep -oE 'customers/details/[0-9a-f-]+' | head -1 | cut -d/ -f3)
check "lock via modal" "$(modal_post "/admin/customers/details/$SAMID" lockModal --data-urlencode 'reason=Session revocation test')" 302
check "Sam's existing session is rejected right away" "$(curl -s -o /dev/null -w '%{http_code}' -b $C $B/dashboard)" 302
check "Sam's API access is cut too" "$(code -b $C $B/api/accounts)" 401
check "unlock via modal" "$(modal_post "/admin/customers/details/$SAMID?tab=security" unlockModal --data-urlencode 'reason=Test finished')" 302

echo "== 3. Interest can't be paid twice in one month (uses brand-new accounts, so test order doesn't matter)"
new_savings() { # nickname, deposit -> prints the new account id
  modal_post "/admin/customers/details/$SAMID" openAccountModal -d type=Savings --data-urlencode "nickname=$1" -d "openingDeposit=$2" --data-urlencode 'reason=Interest guard test' >/dev/null
  curl -s -b $A "$B/admin/accounts?owner=$1" | grep -oE 'accounts/details/[0-9]+' | head -1 | cut -d/ -f3
}
SAV=$(new_savings InterestOne 1200)
check "post interest to a new savings account" "$(modal_post /admin/accounts/details/$SAV interestModal --data-urlencode 'reason=First posting')" 302
has "  ...posted" "$(flash $B/admin/accounts/details/$SAV)" "Posted"
modal_post /admin/accounts/details/$SAV interestModal --data-urlencode 'reason=Second posting' >/dev/null
has "posting again the same month is refused" "$(flash $B/admin/accounts/details/$SAV)" "already posted"
SAV2=$(new_savings InterestTwo 800)
modal_post /admin/settings interestRunModal --data-urlencode 'reason=Monthly run 1' >/dev/null
has "monthly run credits unpaid accounts" "$(flash $B/admin/settings)" "accounts credited"
check "  ...the newest unpaid account got interest" "$(curl -s -b $A "$B/admin/accounts/details/$SAV2" | grep -c 'monthly run')" 1
check "  ...the already-paid account was skipped (still 1 interest row)" "$(curl -s -b $A "$B/admin/accounts/details/$SAV" | grep -c '<td>Interest</td>')" 1
modal_post /admin/settings interestRunModal --data-urlencode 'reason=Monthly run 2' >/dev/null
has "running the monthly job twice is refused" "$(flash $B/admin/settings)" "already paid"
summary
