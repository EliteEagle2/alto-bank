#!/usr/bin/env bash
# PROFILE & SETTINGS — personal info, security, alert settings (muting, low-balance / large-purchase alerts),
# preferences (default account, privacy mode, theme, find-me-by-phone, paperless). Needs B and TEST_DB.
source "$(dirname "$0")/lib.sh"; wait_up
J='Content-Type: application/json'
C=$(mktemp); demo $C customer >/dev/null                     # Jordan
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
A=$(mktemp); demo $A admin >/dev/null
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
first_id() { curl -s -b $1 $B/api/accounts | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
bal() { awk -v a="$(curl -s -b $1 $B/api/accounts/$2 | grep -oE '"balance":[0-9.-]+' | cut -d: -f2)" 'BEGIN{printf "%.2f", a}'; }
alerts_of() { sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$1' AND Type = '$2'"; }
unread_of() { sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$1' AND Type = '$2' AND ReadAt IS NULL"; }
withdraw() { post $1 /transactions/withdraw /transactions/withdraw -d "AccountId=$2&Amount=$3" >/dev/null; }
pp() { post $C /profile "$@"; }   # post <url> [args] as Jordan, token from /profile
JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM)
JCHK=$(first_id $C Checking); JSAV=$(first_id $C Savings)

echo "== Page"
P=$(curl -s -b $C $B/profile)
for s in personal security alerts preferences applications; do has "section #$s" "$P" "id=\"$s\""; done
has "profile strength meter" "$P" "Profile strength"
has "sidebar link still works (/Profile)" "$(curl -s -b $C $B/Profile)" 'id="personal"'
check "anonymous -> login redirect" "$(code $B/profile)" 302
check "POST without anti-forgery token -> 400" "$(code -b $C -X POST -d 'Theme=sunset' $B/Profile/Preferences)" 400

echo "== Personal info"
pp /profile --data-urlencode Personal.FirstName=Jordan --data-urlencode Personal.LastName=Rivera --data-urlencode Personal.Email=demo@altobank.com --data-urlencode "Personal.Phone=12345" >/dev/null
has "invalid phone refused" "$(cat /tmp/post.html)" "valid 10-digit"
pp /profile --data-urlencode Personal.FirstName=Jordan --data-urlencode Personal.LastName=Rivera --data-urlencode Personal.Email=demo@altobank.com --data-urlencode "Personal.Phone=814.555.0142" -d Personal.State=ZZ >/dev/null
has "invalid state refused" "$(cat /tmp/post.html)" "Choose a state"
pp /profile --data-urlencode Personal.FirstName=Jordan --data-urlencode Personal.LastName=Rivera --data-urlencode Personal.Email=maya@altobank.com >/dev/null
has "someone else's email refused" "$(cat /tmp/post.html)" "already in use"
pp /profile --data-urlencode Personal.FirstName=Jordan --data-urlencode Personal.LastName=Rivera --data-urlencode Personal.Email=demo@altobank.com \
   --data-urlencode Personal.PreferredName=Jo --data-urlencode "Personal.Phone=814.555.0142" --data-urlencode "Personal.Street=1200 12th Ave" \
   --data-urlencode "Personal.Apt=Apt 3B" -d Personal.City=Altoona -d Personal.State=PA -d Personal.Zip=16601 >/dev/null
has "saved" "$(flash $C $B/profile)" "Profile updated"
check "phone stored formatted" "$(sql "SELECT PhoneNumber FROM AspNetUsers WHERE Id = '$JID'")" "(814) 555-0142"
has "dashboard greets the preferred name" "$(curl -s -b $C $B/dashboard)" '>Jo <span'
has "top bar shows it too" "$(curl -s -b $C $B/alerts)" 'ms-1">Jo</span>'
check "audited (Profile.Update)" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Profile.Update' AND TargetId = '$JID'")" 1
has "Maya's seeded preferred name" "$(curl -s -b $M $B/dashboard)" '>May <span'

echo "== Security"
pp /Profile/Password --data-urlencode CurrentPassword=wrong --data-urlencode NewPassword=Better123! --data-urlencode ConfirmPassword=Better123! >/dev/null
has "wrong current password refused" "$(flash $C $B/profile)" "Incorrect password"
pp /Profile/Password --data-urlencode CurrentPassword=Demo123! --data-urlencode NewPassword=Better123! --data-urlencode ConfirmPassword=Different1! >/dev/null
has "confirmation must match" "$(flash $C $B/profile)" "do not match"
pp /Profile/Password --data-urlencode CurrentPassword=Demo123! --data-urlencode NewPassword=Better123! --data-urlencode ConfirmPassword=Better123! >/dev/null
has "password changed" "$(flash $C $B/profile)" "Password changed"
check "this session stays signed in" "$(code -b $C $B/dashboard)" 200
check "new password works" "$(login $(mktemp) demo@altobank.com 'Better123!')" "$B/dashboard"
pp /Profile/Password --data-urlencode CurrentPassword=Better123! --data-urlencode NewPassword=Demo123! --data-urlencode ConfirmPassword=Demo123! >/dev/null   # put it back
O=$(mktemp); demo $O customer >/dev/null                     # a second Jordan session ("another device")
check "other session works before" "$(code -b $O $B/dashboard)" 200
pp /Profile/SignOutOthers >/dev/null
has "signed out everywhere else" "$(flash $C $B/profile)" "Signed out everywhere else"
check "...the other session is signed out" "$(code -b $O $B/dashboard)" 302
check "...this one isn't" "$(code -b $C $B/dashboard)" 200
has "recent sign-ins listed" "$(curl -s -b $C $B/profile)" "Demo button"

echo "== Alerts: amounts"
pp /Profile/Alerts -d Enabled=MoneyIn -d LowBalanceAlert=200000 >/dev/null
has "amount above the max refused" "$(flash $C $B/profile)" "between"
pp /Profile/Alerts -d Enabled=Balance -d LargeDebitAlert=10.555 >/dev/null
has "3 decimals refused" "$(flash $C $B/profile)" "2 decimal"
CB=$(bal $C $JCHK); LOW=$(awk -v b="$CB" 'BEGIN{printf "%.2f", b-2}')
pp /Profile/Alerts -d Enabled=MoneyIn -d Enabled=Requests -d Enabled=Bills -d Enabled=Balance -d Enabled=Budgets -d Enabled=Deposits -d Enabled=Disputes -d "LowBalanceAlert=$LOW" -d LargeDebitAlert=4 >/dev/null
has "saved" "$(flash $C $B/profile)" "Alert settings saved"
L0=$(alerts_of $JID LowBalance); G0=$(alerts_of $JID LargeDebit)
withdraw $C $JCHK 1.00
check "small debit: no large-purchase alert" "$(alerts_of $JID LargeDebit)" "$G0"
check "...and still above the low-balance line" "$(alerts_of $JID LowBalance)" "$L0"
withdraw $C $JCHK 5.00
check "large purchase alert (>= \$4)" "$(alerts_of $JID LargeDebit)" "$((G0 + 1))"
check "low balance alert when crossing below \$$LOW" "$(alerts_of $JID LowBalance)" "$((L0 + 1))"
withdraw $C $JCHK 1.00
check "no second low-balance alert while already below" "$(alerts_of $JID LowBalance)" "$((L0 + 1))"
has "alerts link to the account" "$(sql "SELECT Link FROM Alerts WHERE UserId = '$JID' AND Type = 'LowBalance' ORDER BY Id DESC LIMIT 1")" "/accounts/$JCHK"
check "savings unaffected by the threshold until it crosses too" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Type = 'LowBalance' AND Link = '/accounts/$JSAV'")" 0

echo "== Alerts: muting"
pp /Profile/Alerts -d Enabled=MoneyIn -d Enabled=Requests -d LargeDebitAlert=4 >/dev/null   # Balance (and others) now muted
check "muted list saved" "$(sql "SELECT MutedAlerts FROM UserPreferences WHERE UserId = '$JID'")" "Bills,Balance,Budgets,Deposits,Disputes"
U0=$(unread_of $JID LargeDebit); G1=$(alerts_of $JID LargeDebit)
withdraw $C $JCHK 5.00
check "muted alert still recorded in history" "$(alerts_of $JID LargeDebit)" "$((G1 + 1))"
check "...but arrives already read (bell unchanged)" "$(unread_of $JID LargeDebit)" "$U0"
pp /Profile/Alerts -d Enabled=MoneyIn >/dev/null
hasnt "Security can't be muted (not saved)" "$(sql "SELECT MutedAlerts FROM UserPreferences WHERE UserId = '$JID'")" "Security"
F0=$(unread_of $JID AccountFrozen)
post $A "/admin/accounts/details/$JCHK" "/admin/accounts/freeze/$JCHK" --data-urlencode "reason=Profile mute test" >/dev/null
check "freeze alert still lights the bell" "$(unread_of $JID AccountFrozen)" "$((F0 + 1))"
post $A "/admin/accounts/details/$JCHK" "/admin/accounts/unfreeze/$JCHK" --data-urlencode "reason=Profile mute test done" >/dev/null
check "audited (Profile.Alerts)" "$([ "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Profile.Alerts' AND TargetId = '$JID'")" -ge 3 ] && echo yes)" yes
pp /Profile/Alerts -d Enabled=MoneyIn -d Enabled=Requests -d Enabled=Bills -d Enabled=Balance -d Enabled=Budgets -d Enabled=Deposits -d Enabled=Disputes >/dev/null   # back on, amounts off

echo "== Preferences"
pp /Profile/Preferences -d Theme=neon -d Paperless=true -d DiscoverableByPhone=true >/dev/null
has "unknown theme refused" "$(flash $C $B/profile)" "listed themes"
MCHK=$(first_id $M Checking)
pp /Profile/Preferences -d Theme=aurora -d "DefaultAccountId=$MCHK" -d Paperless=true -d DiscoverableByPhone=true >/dev/null
has "someone else's account as default refused" "$(flash $C $B/profile)" "your open accounts"
pp /Profile/Preferences -d Theme=sunset -d "DefaultAccountId=$JSAV" -d HideBalances=true -d DiscoverableByPhone=false >/dev/null
has "saved" "$(flash $C $B/profile)" "Preferences saved"
D=$(curl -s -b $C $B/dashboard)
has "theme applied" "$D" 'data-theme-accent="sunset"'
has "privacy mode on by default" "$D" 'privacy-on'
has "eye toggle in the top bar" "$D" 'data-privacy-toggle'
check "paperless off saved (unchecked box = false)" "$(sql "SELECT Paperless FROM UserPreferences WHERE UserId = '$JID'")" 0
has "default account pre-selected on Transfer" "$(curl -s -b $C $B/transactions/transfer | tr -s ' \n' ' ')" "<option selected=\"selected\" value=\"$JSAV\""
has "...and on Send & Request" "$(curl -s -b $C $B/payments | tr -s ' \n' ' ')" "<option selected=\"selected\" value=\"$JSAV\""
check "not findable by phone any more" "$(code -b $M "$B/api/payments/recipient?q=8145550142")" 404
check "...still findable by email" "$(code -b $M "$B/api/payments/recipient?q=demo@altobank.com")" 200
has "Maya's seeded theme" "$(curl -s -b $M $B/dashboard)" 'data-theme-accent="violet"'
hasnt "other customers keep their own theme" "$(curl -s -b $M $B/dashboard)" 'sunset'
pp /Profile/Preferences -d Theme=aurora -d Paperless=true -d DiscoverableByPhone=true >/dev/null   # restore defaults
check "restored: findable by phone again" "$(code -b $M "$B/api/payments/recipient?q=8145550142")" 200
check "audited (Profile.Preferences)" "$([ "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Profile.Preferences' AND TargetId = '$JID'")" -ge 2 ] && echo yes)" yes

summary
