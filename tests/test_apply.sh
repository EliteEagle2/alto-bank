#!/usr/bin/env bash
# End-to-end test of the 7-step account application (server side), with field REPLACEMENT per test.
B=${B:?set B to the TEST server url}
PASS=0; FAIL=0
ok()   { PASS=$((PASS+1)); echo "  PASS  $1"; }
bad()  { FAIL=$((FAIL+1)); echo "  FAIL  $1"; }
check(){ if [ "$2" = "$3" ]; then ok "$1 ($2)"; else bad "$1: got [$2] expected [$3]"; fi; }
has()  { if echo "$2" | grep -q -- "$3"; then ok "$1"; else bad "$1: missing [$3]"; fi; }
tok()  { curl -s -b $1 -c $1 "$B$2" | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | head -1 | sed 's/.*value="//;s/"$//'; }
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

# ---------- fresh user ----------
U=$(mktemp); EMAIL="apply$RANDOM$RANDOM@test.com"
T=$(tok $U /Auth/Register)
curl -s -b $U -c $U -o /dev/null --data-urlencode "FirstName=Taylor" --data-urlencode "LastName=Brooks" --data-urlencode "Email=$EMAIL" \
  --data-urlencode "Password=Passw0rd!" --data-urlencode "ConfirmPassword=Passw0rd!" --data-urlencode "__RequestVerificationToken=$T" $B/Auth/Register
echo "registered $EMAIL"
T=$(tok $U /Accounts/Apply)
V=$(curl -s -b $U -c $U -X POST -H "RequestVerificationToken: $T" $B/Accounts/VerifyIdentity)
IDTOK=$(echo "$V" | sed 's/.*"token":"\([^"]*\)".*/\1/'); LAST4=$(echo "$V" | sed 's/.*"last4":"\([0-9]*\)".*/\1/')
echo "identity check returned last4=$LAST4"

# ---------- a complete, valid application ----------
declare -A F=(
  [AccountType]=Checking [Nickname]="Daily Driver" [CardDesign]=onyx
  [FirstName]=Taylor [MiddleInitial]=J [LastName]=Brooks [DobMonth]=7 [DobDay]=4 [DobYear]=1995 [Citizenship]=US
  [Phone]="(814) 555-0199" [Street]="500 Logan Blvd" [Apt]="" [City]=Altoona [State]=PA [Zip]=16602
  [LivedTwoYears]=false [PrevStreet]="12 Old Mill Rd" [PrevCity]=Duncansville [PrevState]=PA [PrevZip]=16635
  [EmploymentStatus]=Employed [Employer]="Sheetz Data" [Occupation]=Analyst [IncomeRange]='$200,000 or more'
  [SourceOfFunds]="Employment|Savings"
  [IdentityToken]="$IDTOK" [QuizStreet]="500 logan blvd" [QuizZip]=16602
  [InitialDeposit]=250 [FundingAccountId]="" [ExternalBankName]="First Keystone" [ExternalLast4]=4321
  [AgreeAccountAgreement]=true [AgreePrivacy]=true [AgreeESign]=true [ESignName]="taylor j. brooks"
)
ORDER=(AccountType Nickname CardDesign FirstName MiddleInitial LastName DobMonth DobDay DobYear Citizenship Phone Street Apt City State Zip
  LivedTwoYears PrevStreet PrevCity PrevState PrevZip EmploymentStatus Employer Occupation IncomeRange SourceOfFunds
  IdentityToken QuizStreet QuizZip InitialDeposit FundingAccountId ExternalBankName ExternalLast4
  AgreeAccountAgreement AgreePrivacy AgreeESign ESignName)

# apply k=v ...  → posts the base application with those fields REPLACED.
apply() {
  declare -A O=(); local kv
  for kv in "$@"; do O["${kv%%=*}"]="${kv#*=}"; done
  local ARGS=() k v p
  for k in "${ORDER[@]}"; do
    v="${F[$k]}"; [[ -v O[$k] ]] && v="${O[$k]}"
    if [ "$k" = SourceOfFunds ]; then
      IFS='|' read -ra parts <<< "$v"; for p in "${parts[@]}"; do ARGS+=(--data-urlencode "SourceOfFunds=$p"); done
    else ARGS+=(--data-urlencode "$k=$v"); fi
  done
  local T=$(tok $U /Accounts/Apply)
  curl -s -b $U -c $U -o /tmp/apply_resp.html -w '%{http_code} %{redirect_url}' "${ARGS[@]}" --data-urlencode "__RequestVerificationToken=$T" $B/Accounts/Apply
}
errs() { grep -oE 'class="field-validation-error"[^>]*>[^<]+' /tmp/apply_resp.html | sed 's/.*>//' | sort -u | tr '\n' '|'; grep -oE '<li>[^<]+</li>' /tmp/apply_resp.html | sed 's/<[^>]*>//g' | tr '\n' '|'; }
start() { grep -o 'data-start-step="[0-9]"' /tmp/apply_resp.html | grep -o '[0-9]'; }
expect_err() { # name, expected message text, expected start step, overrides...
  local name="$1" want="$2" step="$3"; shift 3
  local r=$(apply "$@"); local e=$(errs); local s=$(start)
  if [[ "$r" == 200* ]] && [[ "$e" == *"$want"* ]] && [ "$s" = "$step" ]; then ok "$name -> step $s: $e"; else bad "$name: http=$r step=$s errors=$e"; fi
}

echo "== Server-side rejections (each changes ONE field of a valid application)"
expect_err "under 18 -> [MinimumAge(18)]" "You must be 18 or older" 2 DobYear=2012
expect_err "02/30 is not a real date"     "Enter a real date of birth" 2 DobMonth=2 DobDay=30
expect_err "citizenship Other"            "only open accounts for U.S." 2 Citizenship=Other
expect_err "bad first name"               "Use letters" 2 "FirstName=T4ylor"
expect_err "bad phone"                    "valid 10-digit" 3 Phone=12345
expect_err "bad ZIP"                      "exactly 5 digits" 3 Zip=1660
expect_err "unknown state"                "Select your state" 3 State=ZZ
expect_err "prev address missing"         "previous street" 3 PrevStreet=
expect_err "employer missing"             "employer" 4 Employer=
expect_err "no source of funds"           "at least one source" 4 SourceOfFunds=
expect_err "made-up source of funds"      "Choose sources from the list" 4 "SourceOfFunds=Employment|Lottery"
expect_err "bad income range"             "Select your annual income range" 4 "IncomeRange=A billion"
expect_err "no identity token"            "verify your identity" 5 IdentityToken=
expect_err "forged identity token"        "verify your identity" 5 IdentityToken=abc123
expect_err "wrong quiz street"            "street doesn" 5 "QuizStreet=77 Lakeview Dr"
expect_err "wrong quiz ZIP"               "ZIP code doesn" 5 QuizZip=19103
expect_err "external bank missing"        "name of your other bank" 6 ExternalBankName=
expect_err "external last4 bad"           "last 4 digits" 6 ExternalLast4=12
expect_err "deposit 3 decimals"           "2 decimal places" 6 InitialDeposit=1.234
expect_err "deposit too big"              "between \$0 and \$1,000,000" 6 InitialDeposit=2000000
expect_err "deposit negative"             "between \$0 and \$1,000,000" 6 InitialDeposit=-5
expect_err "funding from someone else"    "Choose one of your Alto accounts" 6 FundingAccountId=1
expect_err "disclosure missing"           "Privacy Notice" 7 AgreePrivacy=false
expect_err "e-sign mismatch"              "signature must match" 7 "ESignName=Someone Else"
check "no account created by any rejected application" "$(curl -s -b $U $B/api/accounts | grep -o '"id"' | wc -l | tr -d ' ')" 1

echo "== Valid Checking application (external deposit)"
R=$(apply)
check "submit -> 302" "$(echo $R | cut -d' ' -f1)" 302
DEC=$(echo "$R" | cut -d' ' -f2); echo "  decision url: $DEC"
DP=$(curl -s -b $U -c $U "$DEC")
has "decision: Approved!" "$DP" "Approved!"
has "decision animates on first view" "$DP" 'data-animate="true"'
has "decision: opening balance \$250.00" "$DP" '\$250.00'
has "decision: routing number" "$DP" "031300000"
has "decision: masked account number" "$DP" '\*\*\*\* [0-9]\{4\}'
has "decision: onyx card" "$DP" 'alto-card alto-card--onyx'
has "decision: nickname on card" "$DP" 'data-card-nickname>Daily Driver'
has "decision: funding label" "$DP" "External: First Keystone"
has "decision: copy buttons" "$DP" 'data-copy="031300000"'
check "decision 2nd view not animated" "$(curl -s -b $U "$DEC" | grep -c 'data-animate="true"')" 0
NEWID=$(curl -s -b $U $B/api/accounts | grep -oE '"id":[0-9]+,"type":"Checking","name":"Daily Driver"' | grep -oE '[0-9]+' | head -1)
has "new account history: external opening deposit" "$(curl -s -b $U $B/accounts/$NEWID)" "Opening deposit from First Keystone"
has "success flash carried to the next page" "$(curl -s -b $U $B/Dashboard)" "is open"
PF=$(curl -s -b $U $B/Profile)
check "profile lists 1 application" "$(echo "$PF" | grep -c 'type-badge credit')" 1

echo "== Second application (returning customer) — Savings funded from the new checking"
AP2=$(curl -s -b $U -c $U $B/Accounts/Apply)
has "confirm screen shown" "$AP2" 'data-step="confirm"'
has "prefilled DOB year" "$AP2" 'value="1995"'
has "prefilled phone" "$AP2" 'value="(814) 555-0199"'
has "prefilled employer (from last application)" "$AP2" 'value="Sheetz Data"'
has "prefilled previous address" "$AP2" 'value="12 Old Mill Rd"'
R=$(apply AccountType=Savings Nickname="Rainy Day" InitialDeposit=1000 FundingAccountId=$NEWID)
if [[ "$R" == 200* ]] && [[ "$(errs)" == *"more than the"* ]]; then ok "overdraft from source rejected (step $(start))"; else bad "overdraft: $R $(errs)"; fi
R=$(apply AccountType=Savings Nickname="Rainy Day" InitialDeposit=100 FundingAccountId=$NEWID)
check "valid Savings application -> 302" "$(echo $R | cut -d' ' -f1)" 302
SAVNEW=$(curl -s -b $U $B/api/accounts | grep -oE '"id":[0-9]+,"type":"Savings","name":"Rainy Day"' | grep -oE '[0-9]+' | head -1)
HS=$(curl -s -b $U $B/accounts/$SAVNEW)
has "savings: Transfer In 'Opening deposit'" "$HS" "Transfer In"
has "checking balance now 150" "$(curl -s -b $U $B/api/accounts/$NEWID)" '"balance":150'
HC=$(curl -s -b $U $B/accounts/$NEWID)
has "checking: Transfer Out 'Opening deposit'" "$HC" "Transfer Out"
check "profile lists 2 applications" "$(curl -s -b $U $B/Profile | grep -c 'type-badge credit')" 2

echo "== Isolation"
C=$(mktemp); T=$(tok $C /Auth/Login)
curl -s -b $C -c $C -o /dev/null -d "who=customer&__RequestVerificationToken=$T" $B/Auth/DemoLogin
check "another user can't open this decision page" "$(code -b $C "$DEC")" 302

echo
echo "RESULT: $PASS passed, $FAIL failed"
