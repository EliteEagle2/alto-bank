#!/usr/bin/env bash
# Shared helpers for the Alto Bank end-to-end tests.
B=${B:?set B to the TEST server url (never the real app on 5264)}
PASS=0; FAIL=0
ok()    { PASS=$((PASS+1)); echo "  PASS  $1"; }
bad()   { FAIL=$((FAIL+1)); echo "  FAIL  $1"; }
check() { if [ "$2" = "$3" ]; then ok "$1 ($2)"; else bad "$1: got [$2] expected [$3]"; fi; }
has()   { if printf '%s' "$2" | grep -q -- "$3"; then ok "$1"; else bad "$1: missing [$3]"; fi; }
hasnt() { if printf '%s' "$2" | grep -q -- "$3"; then bad "$1: unexpected [$3]"; else ok "$1"; fi; }
code()  { curl -s -o /dev/null -w '%{http_code}' "$@"; }
wait_up() { for i in $(seq 1 90); do curl -s -o /dev/null $B && return; sleep 1; done; echo "server not up"; exit 1; }
# First antiforgery token on a page (pages can have several forms).
tok()   { curl -s -b $1 -c $1 "$B$2" | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | head -1 | sed 's/.*value="//;s/"$//'; }
# login <jar> <email> <password>
login() { local T=$(tok $1 /Auth/Login); curl -s -b $1 -c $1 -o /dev/null -w '%{redirect_url}' --data-urlencode "Email=$2" --data-urlencode "Password=$3" --data-urlencode "__RequestVerificationToken=$T" $B/Auth/Login; }
# demo <jar> customer|admin
demo()  { local T=$(tok $1 /Auth/Login); curl -s -b $1 -c $1 -o /dev/null -w '%{redirect_url}' -d "who=$2&__RequestVerificationToken=$T" $B/Auth/DemoLogin; }
# post <jar> <page-with-form> <url> [curl args...] -> prints "status redirect" and saves body to /tmp/post.html
post()  { local J=$1 P=$2 U=$3; shift 3; local T=$(tok $J "$P"); curl -s -b $J -c $J -o /tmp/post.html -w '%{http_code} %{redirect_url}' "$@" --data-urlencode "__RequestVerificationToken=$T" "$B$U"; }
summary() { echo; echo "RESULT: $PASS passed, $FAIL failed"; }
