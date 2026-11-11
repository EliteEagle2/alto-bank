#!/usr/bin/env bash
# PART 1 â€” separate admin (Areas/Admin) and customer sides, roles, redirects, access denied, dashboard chart.
source "$(dirname "$0")/lib.sh"; wait_up

echo "== Anonymous"
check "/admin -> login redirect" "$(code $B/admin)" 302
has   "/admin redirect target is login" "$(curl -s -o /dev/null -w '%{redirect_url}' $B/admin)" "/Auth/Login"
check "/dashboard -> login redirect" "$(code $B/dashboard)" 302
check "/api/admin/stats -> 401" "$(code $B/api/admin/stats)" 401
check "/api/accounts -> 401" "$(code $B/api/accounts)" 401
check "old /api/stats is gone (404)" "$(code $B/api/stats)" 404
LP=$(curl -s $B/Auth/Login)
has "demo button: Demo Customer" "$LP" "Demo Customer"
has "demo button: Demo Admin" "$LP" "Demo Admin"

echo "== Customer"
C=$(mktemp)
check "demo customer -> /dashboard" "$(demo $C customer)" "$B/dashboard"
D=$(curl -s -b $C $B/dashboard)
has "dashboard chart canvas" "$D" 'id="flowChart"'
has "dashboard chart data (30 days)" "$(printf '%s' "$D" | grep -o 'data-flows="[^"]*"' | grep -o 'date' | wc -l | tr -d ' ')" "30"
has "Chart.js from cdnjs" "$D" "cdnjs.cloudflare.com/ajax/libs/Chart.js/4.4.1/chart.umd.min.js"
has "account card links to /accounts/{id}" "$D" 'href="/accounts/1"'
has "money in/out totals" "$D" "Money in"
hasnt "no admin links in customer sidebar" "$D" "Admin Dashboard"
check "/accounts/1 opens the account details page" "$(code -b $C $B/accounts/1)" 200
check "customer GET /admin -> 403" "$(code -b $C $B/admin)" 302
AD=$(curl -s -L -b $C $B/admin)
has "customer sees styled Access Denied" "$AD" "Access denied"
has "access denied mentions admin portal" "$AD" "Admin portal is only available to bank staff"
check "access denied page status 403" "$(code -b $C $B/Auth/AccessDenied?ReturnUrl=%2Fadmin)" 403
check "customer /api/admin/stats -> 403" "$(code -b $C $B/api/admin/stats)" 403
check "customer /api/accounts -> 200" "$(code -b $C $B/api/accounts)" 200

echo "== Admin"
A=$(mktemp)
check "demo admin -> /admin" "$(demo $A admin)" "$B/admin"
AP=$(curl -s -b $A $B/admin)
has "admin layout branding" "$AP" "Alto Bank Admin"
has "admin stylesheet" "$AP" "/css/admin.css"
for item in Overview Customers Accounts Transactions Applications Settings "Audit Log"; do has "sidebar: $item" "$AP" "<span>$item</span>"; done
check "stat strip cells (partial)" "$(printf '%s' "$AP" | grep -c 'class="kpi-cell')" 6
has "overview recent applications" "$AP" "Recent applications"
check "admin /api/admin/stats -> 200" "$(code -b $A $B/api/admin/stats)" 200
has "stats JSON" "$(curl -s -b $A $B/api/admin/stats)" '"totalCustomers":3'
check "admin GET /dashboard -> denied" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $A $B/dashboard | grep -c AccessDenied)" 1
check "admin /api/accounts -> 403" "$(code -b $A $B/api/accounts)" 403
check "real admin login (password) -> /admin" "$(login $(mktemp) admin@altobank.com 'Admin123!')" "$B/admin"
check "real customer login (password) -> /dashboard" "$(login $(mktemp) maya@altobank.com 'Demo123!')" "$B/dashboard"

echo "== New registration gets the Customer role"
R=$(mktemp); E="p1$RANDOM$RANDOM@test.com"
post $R /Auth/Register /Auth/Register --data-urlencode FirstName=Pat --data-urlencode LastName=Lee --data-urlencode "Email=$E" \
     --data-urlencode "Password=Passw0rd!" --data-urlencode "ConfirmPassword=Passw0rd!" >/dev/null
check "new user can open /dashboard" "$(code -b $R $B/dashboard)" 200
check "new user denied /admin" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $R $B/admin | grep -c AccessDenied)" 1

echo "== Swagger"
SW=$(curl -s $B/swagger/v1/swagger.json)
has "swagger: /api/admin/stats" "$SW" '"/api/admin/stats"'
has "swagger: /api/accounts" "$SW" '"/api/accounts"'
hasnt "swagger: MVC /accounts/{id} hidden" "$SW" '"/accounts/{id}"'
summary
