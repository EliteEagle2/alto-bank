// An INDEPENDENT amortization schedule for the loan tests, in exact integer math (BigInt cents, exact fractions), so the
// app's decimal LoanMath is checked against something that can't share its bugs.
//   node amortize.js <amount> <aprPercent> <months>
// prints: monthlyPayment finalPayment totalInterest totalOfPayments lastBalance count
// Rules (the standard ones the app documents): M = P·r / (1 - (1+r)^-n), r = APR / 12, rounded half away from zero to
// the cent; each month interest = balance × r (rounded to the cent); payment n (the term) is whatever is left, so the
// cent rounding of M is absorbed there and the loan ends on time at exactly $0.00.
const [amountArg, aprArg, monthsArg] = process.argv.slice(2);
const P = BigInt(Math.round(parseFloat(amountArg) * 100));          // cents
const a = BigInt(Math.round(parseFloat(aprArg) * 100));             // basis points: 6.00% -> 600
const b = 120000n;                                                  // r = a / b  (percent/100/12 in basis points)
const n = parseInt(monthsArg, 10);
const roundDiv = (num, den) => (2n * num + den) / (2n * den);       // half away from zero, for num, den > 0
let M;
if (a === 0n) M = roundDiv(P, BigInt(n));
else {
  const g = (a + b) ** BigInt(n), h = b ** BigInt(n);               // (1+r)^n = g / h
  M = roundDiv(P * a * g, b * (g - h));
}
let bal = P, totalInterest = 0n, totalPaid = 0n, last = 0n, count = 0;
while (bal > 0n && count < 1000) {
  const interest = a === 0n ? 0n : roundDiv(bal * a, b);
  let principal = M - interest;
  if (count + 1 >= n || principal >= bal || principal <= 0n) principal = bal;
  bal -= principal;
  last = interest + principal;
  totalInterest += interest; totalPaid += last; count++;
}
const d = c => (Number(c) / 100).toFixed(2);
console.log([d(M), d(last), d(totalInterest), d(totalPaid), d(bal), count].join(' '));
