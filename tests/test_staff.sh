#!/usr/bin/env bash
# STAFF ACCESS — admins who also bank here, the Owner (set in configuration), adding/removing admins,
# and "admin tools never work on your own accounts". Needs B and TEST_DB (scratch database only).
source "$(dirname "$0")/lib.sh"; wait_up
J='Content-Type: application/json'
A=$(mktemp); demo $A admin >/dev/null                        # Alex Morgan: admin + owner + customer
C=$(mktemp); demo $C customer >/dev/null                     # Jordan
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
apost() { post $A "$@"; }
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
roles_of() { sql "SELECT GROUP_CONCAT(r.Name, ',') FROM (SELECT r.Name FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE ur.UserId = '$1' ORDER BY r.Name) r"; }
first_id() { curl -s -b $1 $B/api/accounts | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
AID=$(uid ADMIN@ALTOBANK.COM); JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM)

echo "== The admin also banks here"
check "seeded admin has Admin, Customer and Owner roles" "$(roles_of $AID)" "Admin,Customer,Owner"
check "admin login still lands on the admin console" "$(demo $(mktemp) admin)" "$B/admin"
check "admin can open the customer dashboard" "$(code -b $A $B/dashboard)" 200
ACHK=$(first_id $A Checking); ASAV=$(first_id $A Savings)
check "admin has their own checking + savings" "$([ -n "$ACHK" ] && [ -n "$ASAV" ] && echo yes)" yes
has "their accounts show on the dashboard" "$(curl -s -b $A $B/dashboard)" "Personal Checking"
check "admin sees only THEIR accounts in the customer API" "$(curl -s -b $A $B/api/accounts | grep -o '"id":' | wc -l | tr -d ' ')" 2
check "admin can't open Jordan's account on the customer site (404)" "$(code -b $A $B/accounts/$(first_id $C Checking))" 404
has "customer sidebar has an Admin console link for staff" "$(curl -s -b $A $B/dashboard)" 'side-link--admin'
hasnt "...but not for regular customers" "$(curl -s -b $C $B/dashboard)" 'Admin console'
has "admin console menu links to 'My banking'" "$(curl -s -b $A $B/admin)" 'My banking'
R=$(curl -s -b $A -H "$J" -d "{\"fromAccountId\":$ACHK,\"toAccountId\":$ASAV,\"amount\":5,\"memo\":\"own transfer\"}" $B/api/transfers)
has "admin can move their own money as a customer" "$R" 'Transferred $5.00'
check "...and it's audited as a CUSTOMER action, not an admin one" "$(sql "SELECT ActorRole FROM AuditLogs WHERE Action = 'Transfer' AND ActorEmail = 'admin@altobank.com' ORDER BY Id DESC LIMIT 1")" Customer

echo "== Admin tools refuse to act on your own records"
apost "/admin/accounts/details/$ACHK" "/admin/accounts/freeze/$ACHK" --data-urlencode "reason=Freezing my own account" >/dev/null
has "can't freeze your own account" "$(flash $A $B/admin/accounts/details/$ACHK)" "your own profile or accounts"
check "...it stays Active" "$(sql "SELECT Status FROM Accounts WHERE Id = $ACHK")" Active
B0=$(sql "SELECT Balance FROM Accounts WHERE Id = $ACHK")
apost "/admin/accounts/details/$ACHK" "/admin/accounts/adjust/$ACHK" -d direction=credit -d amount=1000 --data-urlencode "reason=Giving myself money" >/dev/null
has "can't credit your own account" "$(flash $A $B/admin/accounts/details/$ACHK)" "your own profile or accounts"
check "...balance unchanged" "$(sql "SELECT Balance FROM Accounts WHERE Id = $ACHK")" "$B0"
apost "/admin/customers/details/$AID" "/admin/customers/updateprofile/$AID" --data-urlencode Profile.FirstName=Alexander --data-urlencode Profile.LastName=Morgan --data-urlencode Profile.Email=admin@altobank.com >/dev/null
has "can't edit your own customer profile in the admin console" "$(flash $A $B/admin/customers/details/$AID)" "your own profile or accounts"
check "...name unchanged" "$(sql "SELECT FirstName FROM AspNetUsers WHERE Id = '$AID'")" Alex
check "nothing was audited for the refused actions" "$(sql "SELECT COUNT(*) FROM AuditLogs WHERE Reason IN ('Freezing my own account','Giving myself money')")" 0
JCHK=$(first_id $C Checking)
apost "/admin/accounts/details/$JCHK" "/admin/accounts/freeze/$JCHK" --data-urlencode "reason=Staff test freeze" >/dev/null
has "...while other customers' accounts still work normally" "$(flash $A $B/admin/accounts/details/$JCHK)" "frozen"
apost "/admin/accounts/details/$JCHK" "/admin/accounts/unfreeze/$JCHK" --data-urlencode "reason=Staff test done" >/dev/null

echo "== Staff page"
SP=$(curl -s -b $A -c $A $B/admin/staff)
has "nav link" "$(curl -s -b $A $B/admin)" 'href="/admin/staff"'
has "lists the owner" "$SP" 'status-owner'
has "owner sees the Add admin form" "$SP" 'id="addStaff"'
has "owner badge in the console bar" "$SP" '>Owner<'
check "customer can't open it" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $C $B/admin/staff | grep -ci accessdenied)" 1

echo "== Owner adds an admin (promote Maya)"
apost /admin/staff /admin/staff/add -d Add.Mode=promote --data-urlencode Add.Email=maya@altobank.com --data-urlencode "Add.Reason=" >/dev/null
has "reason required" "$(flash $A $B/admin/staff)" "Reason"
apost /admin/staff /admin/staff/add -d Add.Mode=promote --data-urlencode Add.Email=nobody@nowhere.test --data-urlencode "Add.Reason=Testing promote" >/dev/null
has "unknown email refused" "$(flash $A $B/admin/staff)" "No one has a login"
check "Maya can't open /admin yet" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $M $B/admin | grep -ci accessdenied)" 1
apost /admin/staff /admin/staff/add -d Add.Mode=promote --data-urlencode Add.Email=maya@altobank.com --data-urlencode "Add.Reason=Covering the support desk" >/dev/null
has "promoted" "$(flash $A $B/admin/staff)" "is now an admin"
check "Maya: Admin + Customer" "$(roles_of $MID)" "Admin,Customer"
check "Maya opens /admin on her next request (no re-login)" "$(code -b $M $B/admin)" 200
has "audited (Staff.Grant + reason)" "$(sql "SELECT Reason FROM AuditLogs WHERE Action = 'Staff.Grant' ORDER BY Id DESC LIMIT 1")" "Covering the support desk"

echo "== A plain admin can't manage staff or act on themselves"
MP=$(curl -s -b $M $B/admin/staff)
check "Maya can view the staff list" "$(code -b $M $B/admin/staff)" 200
hasnt "...but gets no Add admin form" "$MP" 'id="addStaff"'
check "Maya POST /admin/staff/add -> access denied" "$(post $M /admin/staff /admin/staff/add -d Add.Mode=create --data-urlencode Add.Email=sneaky@altobank.com -d Add.FirstName=S -d Add.LastName=N --data-urlencode "Add.Reason=Making a friend admin" | grep -ci accessdenied)" 1
check "...no user created" "$(sql "SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = 'SNEAKY@ALTOBANK.COM'")" 0
check "Maya can't revoke the owner" "$(post $M /admin/staff "/admin/staff/revoke/$AID" --data-urlencode "reason=Taking over the bank" | grep -ci accessdenied)" 1
MCHK=$(first_id $M Checking)
post $M "/admin/accounts/details/$MCHK" "/admin/accounts/adjust/$MCHK" -d direction=credit -d amount=500 --data-urlencode "reason=Bonus for me" >/dev/null
has "Maya (now admin) can't credit her own account" "$(flash $M $B/admin/accounts/details/$MCHK)" "your own profile or accounts"

echo "== Owner creates a brand-new staff login"
apost /admin/staff /admin/staff/add -d Add.Mode=create --data-urlencode Add.Email=taylor.staff@altobank.com -d Add.FirstName=Taylor -d Add.LastName=Nguyen --data-urlencode "Add.Reason=New operations hire" >/dev/null
SP=$(curl -s -b $A -c $A $B/admin/staff)
has "created" "$(printf '%s' "$SP" | tr -s ' \n' ' ')" "Taylor Nguyen"
has "temporary password shown once" "$SP" 'data-temp-password'
hasnt "...and only once" "$(curl -s -b $A $B/admin/staff)" 'data-temp-password'
TID=$(uid TAYLOR.STAFF@ALTOBANK.COM)
check "new staff: Admin only (not a customer)" "$(roles_of $TID)" "Admin"
check "must change password at first login" "$(sql "SELECT MustChangePassword FROM AspNetUsers WHERE Id = '$TID'")" 1
TEMP=$(printf '%s' "$SP" | grep -oE 'data-temp-password>[^<]+' | sed 's/.*>//')
T=$(mktemp); login $T taylor.staff@altobank.com "$TEMP" >/dev/null
post $T /Auth/ChangePassword /Auth/ChangePassword --data-urlencode "CurrentPassword=$TEMP" --data-urlencode "NewPassword=Staff123!x" --data-urlencode "ConfirmPassword=Staff123!x" >/dev/null
check "staff-only login opens the admin console" "$(code -b $T $B/admin)" 200
check "...but NOT online banking (not a customer)" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $T $B/dashboard | grep -ci accessdenied)" 1
check "...nor the customer API" "$(code -b $T $B/api/accounts)" 403
apost /admin/staff /admin/staff/add -d Add.Mode=create --data-urlencode Add.Email=maya@altobank.com -d Add.FirstName=M -d Add.LastName=C --data-urlencode "Add.Reason=Duplicate email test" >/dev/null
has "can't create a second login for an existing email" "$(flash $A $B/admin/staff)" "already has a login"

echo "== Removing admin access"
apost /admin/staff "/admin/staff/revoke/$AID" --data-urlencode "reason=Trying to remove the owner" >/dev/null
has "the owner can't be removed from the website" "$(flash $A $B/admin/staff)" "configuration"
apost /admin/staff "/admin/staff/revoke/$MID" --data-urlencode "reason=" >/dev/null
has "revoke needs a reason" "$(flash $A $B/admin/staff)" "reason of at least"
apost /admin/staff "/admin/staff/revoke/$MID" --data-urlencode "reason=Support desk cover ended" >/dev/null
has "revoked" "$(flash $A $B/admin/staff)" "no longer has admin access"
check "Maya is a customer again" "$(roles_of $MID)" "Customer"
check "...locked out of /admin on her next request" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $M $B/admin | grep -ci accessdenied)" 1
check "...but her banking still works" "$(code -b $M $B/dashboard)" 200
check "her login was kept (nothing deleted)" "$(sql "SELECT COUNT(*) FROM AspNetUsers WHERE Id = '$MID'")" 1
has "audited (Staff.Revoke)" "$(sql "SELECT Reason FROM AuditLogs WHERE Action = 'Staff.Revoke' ORDER BY Id DESC LIMIT 1")" "Support desk cover ended"
check "exactly one owner" "$(sql "SELECT COUNT(*) FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE r.Name = 'Owner'")" 1

summary
