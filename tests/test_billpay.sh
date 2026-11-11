#!/usr/bin/env bash
# FEATURE 2 — Bill Pay + scheduled/recurring transfers + the background scheduler.
# Needs B, TEST_DB, and a test server started with Scheduler__IntervalSeconds=5.
source "$(dirname "$0")/lib.sh"; wait_up
J='Content-Type: application/json'
C=$(mktemp); demo $C customer >/dev/null                    # Jordan
M=$(mktemp); login $M maya@altobank.com 'Demo123!' >/dev/null
A=$(mktemp); demo $A admin >/dev/null
apost() { post $A "$@"; }
first_id() { curl -s -b $1 $B/api/accounts | grep -oE "\"id\":[0-9]+,\"type\":\"$2\"" | sed -n "${3:-1}p" | grep -oE '[0-9]+'; }
bal() { awk -v a="$(curl -s -b $1 $B/api/accounts/$2 | grep -oE '"balance":[0-9.-]+' | cut -d: -f2)" 'BEGIN{printf "%.2f", a}'; }
add() { awk -v a="$1" -v b="$2" 'BEGIN{printf "%.2f", a+b}'; }
uid() { sql "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = '$1'"; }
TODAY=$(sql "SELECT date('now','localtime')")   # the test machine is in the bank's time zone (America/New_York)
TOMORROW=$(sql "SELECT date('now','localtime','+1 day')")
DOW=$(sql "SELECT CASE strftime('%w','now','localtime') WHEN '0' THEN 'Sunday' WHEN '1' THEN 'Monday' WHEN '2' THEN 'Tuesday' WHEN '3' THEN 'Wednesday' WHEN '4' THEN 'Thursday' WHEN '5' THEN 'Friday' ELSE 'Saturday' END")
runnow() { apost /admin/scheduled /admin/scheduled/runnow --data-urlencode "reason=${1:-Test run of the scheduler}" >/dev/null; }
pay() {   # pay <jar> <payeeId> <fromId> <amount> <when> [extra curl args] -> flash
  local jar=$1 payee=$2 from=$3 amt=$4 when=$5; shift 5
  post $jar /bills /bills/pay -d "Pay.PayeeId=$payee" -d "Pay.FromAccountId=$from" -d "Pay.Amount=$amt" -d "Pay.When=$when" "$@" >/dev/null
  flash $jar $B/bills
}
last_sched() { sql "SELECT MAX(Id) FROM ScheduledPayments WHERE UserId = '$JID'"; }
runs_for() { sql "SELECT COUNT(*) FROM ScheduledRuns WHERE ScheduledPaymentId = $1"; }

JID=$(uid DEMO@ALTOBANK.COM); MID=$(uid MAYA@ALTOBANK.COM)
JCHK=$(first_id $C Checking); JSAV=$(first_id $C Savings 1); MCHK=$(first_id $M Checking)
echo "today=$TODAY ($DOW); Jordan checking=$JCHK savings=$JSAV; Maya checking=$MCHK"

echo "== Access + seeded demo"
check "anon /bills -> login" "$(curl -s -o /dev/null -w '%{redirect_url}' $B/bills | grep -c /Auth/Login)" 1
check "admin -> /bills denied" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $A $B/bills | grep -ci accessdenied)" 1
BP=$(curl -s -b $C $B/bills)
has "sidebar: Bill Pay" "$BP" 'href="/bills"'
has "sidebar: Upcoming" "$BP" 'href="/scheduled"'
check "seeded: Jordan has 3 payees" "$(echo "$BP" | grep -c 'class="glass payee-card"')" 3
has "seeded demo payee" "$BP" "Keystone Electric (demo)"
UP=$(curl -s -b $C $B/scheduled)
has "upcoming: weekly transfer" "$UP" "Every Friday"
has "upcoming: calendar grid" "$UP" 'class="cal-grid"'
has "upcoming: seeded bill paid today shows in recent runs" "$UP" "Succeeded"
has "seeded failure shows for Samuel (admin failures view)" "$(curl -s -b $A "$B/admin/scheduled?failures=true")" "Insufficient funds"
has "Jordan got a 'Bill paid' alert" "$(curl -s -b $C $B/alerts)" "Bill paid"

echo "== Payees (soft delete, ownership)"
post $C /bills /bills/payees --data-urlencode "NewPayee.Name=Test Gas Co" -d "NewPayee.Category=Utilities" -d "NewPayee.AccountNumber=TG-1234" >/dev/null
has "add payee" "$(flash $C $B/bills)" "Test Gas Co was added"
GAS=$(sql "SELECT Id FROM Payees WHERE Name = 'Test Gas Co'")
post $C /bills /bills/payees --data-urlencode "NewPayee.Name=Bad" -d "NewPayee.Category=Other" -d "NewPayee.AccountNumber=ab" >/dev/null
has "account number validated" "$(flash $C $B/bills)" "4"
post $C /bills "/bills/payees/$GAS" --data-urlencode "Name=Test Gas Co" -d "Category=Utilities" -d "AccountNumber=TG-1234" --data-urlencode "Nickname=Gas" >/dev/null
has "edit payee" "$(flash $C $B/bills)" "Gas was updated"
check "another customer can't edit it -> 404" "$(post $M /bills "/bills/payees/$GAS" --data-urlencode "Name=Hijack" -d "Category=Other" -d "AccountNumber=HJ-0000" | cut -d' ' -f1)" 404
check "another customer can't remove it -> 404" "$(post $M /bills "/bills/payees/$GAS/remove" | cut -d' ' -f1)" 404
has "another customer can't pay it" "$(pay $M $GAS $MCHK 1 now)" "Payee not found"

echo "== Pay now"
J0=$(bal $C $JCHK)
has "pay now" "$(pay $C $GAS $JCHK 12.34 now --data-urlencode 'Pay.Memo=Oct gas')" 'Paid $12.34 to Gas'
check "balance down" "$(bal $C $JCHK)" "$(add $J0 -12.34)"
TX=$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?pageSize=1")
has "BillPayment row" "$TX" '"kind":"BillPayment"'
has "counterparty = payee name" "$TX" '"counterparty":"Test Gas Co"'
has "kind=bill filter" "$(curl -s -b $C "$B/api/accounts/$JCHK/transactions?kind=bill&pageSize=50")" '"description":"Bill payment - Test Gas Co - Oct gas"'
has "insufficient funds" "$(pay $C $GAS $JSAV 999999 now)" "Insufficient funds\|daily transfer limit"
has "someone else's account" "$(pay $C $GAS $MCHK 1 now)" "Account not found"
apost "/admin/accounts/details/$JCHK" "/admin/accounts/freeze/$JCHK" --data-urlencode "reason=Bill pay freeze test" >/dev/null
has "frozen account can't pay" "$(pay $C $GAS $JCHK 1 now)" "frozen"
apost "/admin/accounts/details/$JCHK" "/admin/accounts/unfreeze/$JCHK" --data-urlencode "reason=Done testing" >/dev/null
USED=$(curl -s -b $C $B/accounts/$JCHK | tr -s ' \n' ' ' | grep -oE '\(<span class="money[^>]*>\$[0-9.,]+</span> used today' | grep -oE '[0-9][0-9.,]*' | head -1 | tr -d ,)
apost "/admin/accounts/details/$JCHK" "/admin/accounts/updatesettings/$JCHK" --data-urlencode "nickname=Everyday Checking" -d "dailyLimit=$(add $USED 20)" --data-urlencode "reason=Bill pay limit test" >/dev/null
has "bill payments count toward the daily limit: 15 ok" "$(pay $C $GAS $JCHK 15 now)" 'Paid $15.00'
has "...then 10 more is over" "$(pay $C $GAS $JCHK 10 now)" "daily transfer limit"
has "...and transfers share the same limit" "$(curl -s -b $C -H "$J" -d "{\"fromAccountId\":$JCHK,\"toAccountId\":$JSAV,\"amount\":10}" $B/api/transfers)" "daily transfer limit"

echo "== Scheduled bill: runs once, idempotent, never retried"
has "a scheduled payment that would go over the limit FAILS when it runs" "$(pay $C $GAS $JCHK 10 once -d "Pay.Date=$TODAY")" "Scheduled"
LIM=$(last_sched); runnow
check "...run marked Failed (over the limit)" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $LIM")" Failed
has "...with the reason" "$(sql "SELECT FailureReason FROM ScheduledRuns WHERE ScheduledPaymentId = $LIM")" "daily transfer limit"
apost "/admin/accounts/details/$JCHK" "/admin/accounts/updatesettings/$JCHK" --data-urlencode "nickname=Everyday Checking" -d dailyLimit= --data-urlencode "reason=Back to default" >/dev/null
has "schedule for tomorrow" "$(pay $C $GAS $JCHK 7.77 once -d "Pay.Date=$TOMORROW")" "Scheduled"
TOM=$(last_sched)
check "next date = tomorrow" "$(sql "SELECT NextRunDate FROM ScheduledPayments WHERE Id = $TOM")" "$TOMORROW"
J1=$(bal $C $JCHK)
has "schedule for today" "$(pay $C $GAS $JCHK 5.55 once -d "Pay.Date=$TODAY" --data-urlencode 'Pay.Memo=today')" "Scheduled"
NOW=$(last_sched)
runnow
check "today's item paid by the scheduler" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $NOW")" Succeeded
check "balance down once" "$(bal $C $JCHK)" "$(add $J1 -5.55)"
check "one-time item now Completed" "$(sql "SELECT Status FROM ScheduledPayments WHERE Id = $NOW")" Completed
check "tomorrow's item untouched" "$(runs_for $TOM)" 0
check "the run is linked to its transaction" "$(sql "SELECT COUNT(*) FROM ScheduledRuns r JOIN Transactions t ON t.Id = r.TransactionId WHERE r.ScheduledPaymentId = $NOW AND t.Type = 'BillPayment'")" 1
runnow
check "running again pays nothing more" "$(bal $C $JCHK)" "$(add $J1 -5.55)"
sql "UPDATE ScheduledPayments SET Status = 'Active', NextRunDate = '$TODAY' WHERE Id = $NOW" >/dev/null   # pretend the item's update was lost
runnow
check "even if the item says it's still due, the (item, date) run row stops a second payment" "$(runs_for $NOW)" 1
check "...balance unchanged" "$(bal $C $JCHK)" "$(add $J1 -5.55)"
check "the database has a UNIQUE (item, date) index" "$(sql "SELECT COUNT(*) FROM pragma_index_list('ScheduledRuns') WHERE \"unique\" = 1 AND name = 'IX_ScheduledRuns_ScheduledPaymentId_RunDate'")" 1
DUP=$(sql "INSERT INTO ScheduledRuns (ScheduledPaymentId, RunDate, Status, Amount, ProcessedAt) VALUES ($NOW, '$TODAY', 'Succeeded', 1, datetime('now'))" 2>&1)
has "...which refuses a duplicate run row" "$DUP" "UNIQUE constraint failed"

echo "== Failures: marked Failed with a reason, alert, no retry"
JVAC=$(first_id $C Savings 2); S0=$(bal $C $JVAC); FU=$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Type = 'BillFailed'")
has "schedule more than the balance for today" "$(pay $C $GAS $JVAC $(add $S0 1) once -d "Pay.Date=$TODAY")" "Scheduled"
BIG=$(last_sched); runnow
check "run Failed" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $BIG")" Failed
has "reason saved" "$(sql "SELECT FailureReason FROM ScheduledRuns WHERE ScheduledPaymentId = $BIG")" "Insufficient funds"
check "one-time item Failed" "$(sql "SELECT Status FROM ScheduledPayments WHERE Id = $BIG")" Failed
check "no money moved" "$(bal $C $JVAC)" "$S0"
check "failure alert created" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Type = 'BillFailed'")" "$((FU + 1))"
runnow
check "not retried on the next run" "$(runs_for $BIG)" 1
has "customer sees it in recent runs" "$(curl -s -b $C $B/scheduled)" "Insufficient funds"
# A RECURRING item whose run fails keeps going: Failed run recorded, next date moves on.
post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JSAV" -d "Transfer.ToAccountId=$JCHK" -d "Transfer.Amount=1" -d "Transfer.When=monthly" -d "Transfer.Date=$TODAY" -d "Transfer.DayOfMonth=28" >/dev/null
REC=$(last_sched)
apost "/admin/accounts/details/$JSAV" "/admin/accounts/freeze/$JSAV" --data-urlencode "reason=Recurring failure test" >/dev/null
sql "UPDATE ScheduledPayments SET NextRunDate = '$TODAY' WHERE Id = $REC" >/dev/null
runnow
has "recurring run from a frozen account fails" "$(sql "SELECT FailureReason FROM ScheduledRuns WHERE ScheduledPaymentId = $REC")" "frozen"
check "...but the item stays Active" "$(sql "SELECT Status FROM ScheduledPayments WHERE Id = $REC")" Active
check "...and moves to its next date" "$([ "$(sql "SELECT NextRunDate FROM ScheduledPayments WHERE Id = $REC")" \> "$TODAY" ] && echo later)" later
apost "/admin/accounts/details/$JSAV" "/admin/accounts/unfreeze/$JSAV" --data-urlencode "reason=Done testing" >/dev/null

echo "== Recurring transfers, skip next, month ends"
C0=$(bal $C $JCHK); V0=$(bal $C $JSAV)
post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JCHK" -d "Transfer.ToAccountId=$JSAV" -d "Transfer.Amount=3.00" -d "Transfer.When=weekly" -d "Transfer.Date=$TODAY" -d "Transfer.DayOfWeek=$DOW" --data-urlencode "Transfer.Memo=Weekly test" >/dev/null
has "weekly transfer scheduled" "$(flash $C $B/scheduled)" "every ${DOW,,}"
WK=$(last_sched)
check "first run is today" "$(sql "SELECT NextRunDate FROM ScheduledPayments WHERE Id = $WK")" "$TODAY"
runnow
check "weekly transfer ran" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $WK")" Succeeded
check "money moved out of checking" "$(bal $C $JCHK)" "$(add $C0 -3)"
check "...and into savings" "$(bal $C $JSAV)" "$(add $V0 3)"
check "next run in 7 days" "$(sql "SELECT NextRunDate FROM ScheduledPayments WHERE Id = $WK")" "$(sql "SELECT date('now','localtime','+7 day')")"
post $C /scheduled "/scheduled/$WK/skip" >/dev/null
has "skip next" "$(flash $C $B/scheduled)" "will be skipped"
sql "UPDATE ScheduledRuns SET RunDate = date('now','localtime','-7 day') WHERE ScheduledPaymentId = $WK; UPDATE ScheduledPayments SET NextRunDate = '$TODAY' WHERE Id = $WK" >/dev/null   # time travel: last run was a week ago, the next one is today (scratch DB only)
runnow
check "skipped run recorded" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $WK AND RunDate = '$TODAY'")" Skipped
check "no money moved on a skip" "$(bal $C $JCHK)" "$(add $C0 -3)"
check "skip flag cleared" "$(sql "SELECT SkipNext FROM ScheduledPayments WHERE Id = $WK")" 0
post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JCHK" -d "Transfer.ToAccountId=$JSAV" -d "Transfer.Amount=1" -d "Transfer.When=monthly" -d "Transfer.Date=$TOMORROW" -d "Transfer.DayOfMonth=31" >/dev/null
EOM=$(last_sched)
check "day 31 = the last day of a shorter month" "$(sql "SELECT NextRunDate FROM ScheduledPayments WHERE Id = $EOM")" "$(sql "SELECT CASE WHEN date('now','localtime','+1 day') <= date('now','localtime','start of month','+1 month','-1 day') THEN date('now','localtime','start of month','+1 month','-1 day') ELSE date('now','localtime','start of month','+2 month','-1 day') END")"
post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JCHK" -d "Transfer.ToAccountId=$JCHK" -d "Transfer.Amount=1" -d "Transfer.When=once" -d "Transfer.Date=$TOMORROW" >/dev/null
has "same account twice refused" "$(flash $C $B/scheduled)" "two different accounts"
post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JCHK" -d "Transfer.ToAccountId=$JSAV" -d "Transfer.Amount=1" -d "Transfer.When=once" -d "Transfer.Date=2020-01-01" >/dev/null
has "past date refused" "$(flash $C $B/scheduled)" "in the past"
post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JCHK" -d "Transfer.ToAccountId=$MCHK" -d "Transfer.Amount=1" -d "Transfer.When=once" -d "Transfer.Date=$TOMORROW" >/dev/null
has "can't schedule a transfer to someone else's account" "$(flash $C $B/scheduled)" "Account not found"
NEXTMONTH=$(sql "SELECT strftime('%Y-%m', date('now','localtime','start of month','+1 month'))")
has "calendar for next month shows the month-end transfer" "$(curl -s -b $C "$B/scheduled?month=$NEXTMONTH")" 'cal-item'

echo "== Edit / cancel (owner only)"
post $C /scheduled "/scheduled/$TOM/edit" -d "FromAccountId=$JCHK" -d "Amount=8.88" -d "NextRunDate=$TOMORROW" --data-urlencode "Memo=edited" >/dev/null
has "edit amount" "$(flash $C $B/scheduled)" 'Updated. Next: $8.88'
post $C /scheduled "/scheduled/$TOM/edit" -d "FromAccountId=$JCHK" -d "Amount=8.88" -d "NextRunDate=2020-01-01" >/dev/null
has "edit to a past date refused" "$(flash $C $B/scheduled)" "in the past"
check "another customer's edit page -> 404" "$(code -b $M $B/scheduled/$TOM/edit)" 404
check "another customer's edit POST -> 404" "$(post $M /scheduled "/scheduled/$TOM/edit" -d "FromAccountId=$MCHK" -d "Amount=1" -d "NextRunDate=$TOMORROW" | cut -d' ' -f1)" 404
check "another customer's skip -> 404" "$(post $M /scheduled "/scheduled/$TOM/skip" | cut -d' ' -f1)" 404
check "another customer's unskip -> 404" "$(post $M /scheduled "/scheduled/$TOM/unskip" | cut -d' ' -f1)" 404
check "another customer's cancel -> 404" "$(post $M /scheduled "/scheduled/$TOM/cancel" | cut -d' ' -f1)" 404
check "...and it's untouched" "$(sql "SELECT Status || ' ' || Amount FROM ScheduledPayments WHERE Id = $TOM")" "Active 8.88"
post $C /scheduled "/scheduled/$TOM/edit" -d "FromAccountId=$JCHK" -d "Amount=8.88" -d "NextRunDate=2099-01-01" >/dev/null
has "edit more than a year ahead refused" "$(flash $C $B/scheduled)" "up to a year ahead"
post $C /scheduled "/scheduled/$WK/edit" -d "FromAccountId=$JCHK" -d "Amount=3" -d "NextRunDate=$TOMORROW" >/dev/null
has "weekly item: next date must stay on its weekday" "$(flash $C $B/scheduled)" "must be a $DOW"
post $C /scheduled "/scheduled/$WK/edit" -d "FromAccountId=$JCHK" -d "Amount=3" -d "NextRunDate=$TODAY" >/dev/null
has "a date that already ran can't be reused" "$(flash $C $B/scheduled)" "already processed"
post $C /scheduled "/scheduled/$TOM/cancel" >/dev/null
has "owner cancels" "$(flash $C $B/scheduled)" "Cancelled"
check "status Cancelled, row kept" "$(sql "SELECT Status FROM ScheduledPayments WHERE Id = $TOM")" Cancelled
post $C /bills "/bills/payees/$GAS/remove" >/dev/null
has "remove payee" "$(flash $C $B/bills)" "was removed"
check "payee soft-deleted (row kept, status Removed)" "$(sql "SELECT Status FROM Payees WHERE Id = $GAS")" Removed
check "its active schedules were cancelled" "$(sql "SELECT COUNT(*) FROM ScheduledPayments WHERE PayeeId = $GAS AND Status = 'Active'")" 0
has "a removed payee can't be paid" "$(pay $C $GAS $JCHK 1 now)" "Payee not found"
hasnt "removed payee hidden from the list" "$(curl -s -b $C $B/bills)" "Test Gas Co"

echo "== The background timer pays without anyone clicking"
ELEC=$(sql "SELECT Id FROM Payees WHERE UserId = '$JID' AND Name = 'Keystone Electric (demo)'")
has "schedule a bill for today" "$(pay $C $ELEC $JCHK 2.22 once -d "Pay.Date=$TODAY" --data-urlencode 'Pay.Memo=timer')" "Scheduled"
TIMER=$(last_sched)
for i in $(seq 1 30); do [ "$(runs_for $TIMER)" = 1 ] && break; sleep 1; done
check "paid by the 5-second timer (no admin click)" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $TIMER")" Succeeded
check "last run time is recent" "$(sql "SELECT (julianday('now') - julianday(SchedulerLastRunAt)) * 86400 < 30 FROM BankSettings")" 1
has "admin page shows the last run" "$(curl -s -b $A $B/admin/scheduled)" "Last run:"

echo "== Missed dates after downtime: only the latest is paid"
NEXTWEEK=$(sql "SELECT date('now','localtime','+7 day')"); AGO21=$(sql "SELECT date('now','localtime','-21 day')")
post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JCHK" -d "Transfer.ToAccountId=$JSAV" -d "Transfer.Amount=2.00" -d "Transfer.When=weekly" -d "Transfer.Date=$NEXTWEEK" -d "Transfer.DayOfWeek=$DOW" --data-urlencode "Transfer.Memo=catch-up test" >/dev/null
CU=$(last_sched); C1=$(bal $C $JCHK); MA=$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Scheduled payments missed'")
sql "UPDATE ScheduledPayments SET NextRunDate = '$AGO21' WHERE Id = $CU" >/dev/null   # time travel: the app was off for 3 weeks
runnow "Catch-up test run"
for i in $(seq 1 20); do [ "$(runs_for $CU)" = 2 ] && break; sleep 1; done
check "two run rows: one 'missed', one paid" "$(runs_for $CU)" 2
check "the older dates are recorded as missed (Skipped)" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $CU AND RunDate = '$AGO21'")" Skipped
has "...with the reason" "$(sql "SELECT FailureReason FROM ScheduledRuns WHERE ScheduledPaymentId = $CU AND RunDate = '$AGO21'")" "Missed while the scheduler was offline: 3 date(s)"
check "only today's date was paid" "$(sql "SELECT Status FROM ScheduledRuns WHERE ScheduledPaymentId = $CU AND RunDate = '$TODAY'")" Succeeded
check "money moved once, not 4 times" "$(bal $C $JCHK)" "$(add $C1 -2)"
check "next date is next week" "$(sql "SELECT NextRunDate FROM ScheduledPayments WHERE Id = $CU")" "$NEXTWEEK"
check "customer alerted about the missed dates" "$(sql "SELECT COUNT(*) FROM Alerts WHERE UserId = '$JID' AND Title = 'Scheduled payments missed'")" "$((MA + 1))"
check "scheduler work is audited as System" "$(sql "SELECT ActorRole FROM AuditLogs WHERE Action = 'Schedule.Missed' ORDER BY Id DESC LIMIT 1")" System
check "...including the money movement, even when an admin started the run" "$(sql "SELECT ActorRole FROM AuditLogs WHERE Action = 'Transfer' ORDER BY Id DESC LIMIT 1")" System
has "the admin's request is audited under the admin, with the reason" "$(sql "SELECT ActorEmail || ' ' || Reason FROM AuditLogs WHERE Action = 'Scheduler.RunRequested' ORDER BY Id DESC LIMIT 1")" "admin@altobank.com Catch-up test run"

echo "== Admin page"
AP=$(curl -s -b $A $B/admin/scheduled)
has "sidebar link" "$AP" 'href="/admin/scheduled"'
has "lists items with customer names" "$AP" "Jordan Rivera"
has "status filter" "$(curl -s -b $A "$B/admin/scheduled?status=Cancelled")" "Cancelled"
has "failures filter" "$(curl -s -b $A "$B/admin/scheduled?failures=true&customer=Rivera")" "Insufficient funds"
apost /admin/scheduled /admin/scheduled/runnow -d reason= >/dev/null
has "run now needs a reason" "$(flash $A $B/admin/scheduled)" "reason of at least"
runnow "Manual run for testing"
has "run now flash" "$(flash $A $B/admin/scheduled)" "Scheduler ran"
has "audited" "$(curl -s -b $A "$B/admin/auditlog?action=Scheduler.Run")" "Manual run for testing"
has "admin runs listed" "$(curl -s -b $A $B/admin/scheduled)" "Admin (admin@altobank.com)"
check "customer can't run it" "$(curl -s -o /dev/null -w '%{redirect_url}' -b $C -X POST $B/admin/scheduled/runnow | grep -ci 'accessdenied\|login')" 1

echo "== Scheduler + transfers at the same time (SQLite WAL + busy timeout)"
check "journal mode is WAL" "$(sql "PRAGMA journal_mode")" wal
for i in 1 2 3 4 5; do post $C /scheduled /scheduled/transfer -d "Transfer.FromAccountId=$JCHK" -d "Transfer.ToAccountId=$JSAV" -d "Transfer.Amount=0.$i$i" -d "Transfer.When=once" -d "Transfer.Date=$TODAY" --data-urlencode "Transfer.Memo=conc $i" >/dev/null; done
CONC=$(sql "SELECT GROUP_CONCAT(Id) FROM ScheduledPayments WHERE Memo LIKE 'conc %'")
T0=$(add $(bal $C $JCHK) $(bal $C $JSAV))
rm -f /tmp/conc_*.txt
for i in $(seq 1 16); do (echo "$(code -b $C -H "$J" -d "{\"fromAccountId\":$JCHK,\"toAccountId\":$JSAV,\"amount\":0.01}" $B/api/transfers)" > /tmp/conc_t$i.txt) & done
for i in 1 2 3; do (post $A /admin/scheduled /admin/scheduled/runnow --data-urlencode "reason=Concurrency test $i" | cut -d' ' -f1 > /tmp/conc_r$i.txt) & done
wait
check "no server errors under load" "$(cat /tmp/conc_*.txt | grep -c '^5')" 0
check "every transfer answered 200 (busy timeout waits instead of 'database is locked')" "$(cat /tmp/conc_t*.txt | grep -c '^200$')" 16
for i in $(seq 1 20); do [ "$(sql "SELECT COUNT(*) FROM ScheduledRuns WHERE ScheduledPaymentId IN ($CONC)")" = 5 ] && break; sleep 1; done
check "each due item ran exactly once despite 3 parallel runs + the timer" "$(sql "SELECT COUNT(*) FROM ScheduledRuns WHERE ScheduledPaymentId IN ($CONC)")" 5
check "money only moved between Jordan's accounts (total unchanged)" "$(add $(bal $C $JCHK) $(bal $C $JSAV))" "$T0"

summary
