#!/usr/bin/env python3
"""Synthetic input for Bank.DataImport: the six CSV files CsvSource reads, with the default column names.

Every person, address and amount is invented (example.com emails, "Example Street" addresses, +46 70 000 numbers), so
the files hold no personal data. The history is built to pass the import's selection (at least 30 customers with a
loan, 10 with late payments, 15 with standing orders) and to stay inside the loan products' amount and term ranges
(docs/implementation-decisions.md). Source amounts are SEK / 10, because the import multiplies by Import:AmountFactor
(10.0). Dates start on 2024-01-01; the import shifts them so the first one lands on the bank's current business date.

Kinds of customers:
  - everyday: salary into the current account, card withdrawals, rent and transfers, some standing orders;
  - on-time borrowers: as everyday, plus a loan whose installments are paid into the repayment account before the due date;
  - late borrowers: salary into savings, so the current (repayment) account holds only the loan payments, often paid
    6 to 25 days after the due date (penalties from day six);
  - defaulters: late borrowers who stop paying more than 91 days before the end, so the loan reaches Default.

Usage (from core_banking/bank-data-import):
  python3 tools/generate-sample-data.py                 # 100 customers, 1 year, into data/raw/
  python3 tools/generate-sample-data.py --years 2 --seed 7 --out /tmp/bank-data
"""

import argparse
import calendar
import csv
import math
import random
from datetime import date, timedelta
from pathlib import Path

FIRST_NAMES = [
    "Anna", "Erik", "Sara", "Johan", "Maria", "Lars", "Elin", "Oskar", "Karin", "Nils", "Ida", "Gustav", "Linnea",
    "Viktor", "Maja", "Axel", "Sofia", "Emil", "Hanna", "Leo", "Darya", "Reza", "Nasrin", "Kian", "Leila", "Arash",
    "Shirin", "Omid", "Mina", "Babak", "Emma", "Liam", "Olivia", "Noah", "Grace", "Jack", "Chloe", "Henry", "Amira",
    "Yusuf", "Mei", "Lucas", "Nora", "Felix", "Alva", "Hugo", "Selma", "Theo", "Ebba", "Adam",
]
LAST_NAMES = [
    "Lindqvist", "Holm", "Sandberg", "Ekstrom", "Lund", "Nyberg", "Bergman", "Falk", "Strand", "Wiklund", "Hedlund",
    "Sjoberg", "Moradi", "Karimi", "Tehrani", "Rahimi", "Sadeghi", "Hosseini", "Ahmadi", "Navabi", "Carter", "Bennett",
    "Hughes", "Turner", "Brooks", "Hayes", "Morgan", "Reed", "Foster", "Ward",
]

# Product ranges in SEK (docs/implementation-decisions.md, loan products); rates as fractions.
PRODUCTS = {
    "CAR": {"types": ["Car loan", "Auto loan", "Vehicle financing"], "amount": (40_000, 450_000), "term": (24, 84), "rate": 0.0695},
    "MORTGAGE": {"types": ["Home mortgage", "House mortgage"], "amount": (800_000, 4_000_000), "term": (180, 360), "rate": 0.0425},
    "PURCHASE": {"types": ["Personal loan", "Consumer purchase", "Furniture purchase"], "amount": (15_000, 250_000), "term": (6, 24), "rate": 0.085},
}
PURCHASE_MAX_STAGES = 4
FACTOR = 10  # Import:AmountFactor


def add_months(d: date, months: int) -> date:
    """Like DateOnly.AddMonths: clamps to the month's last day."""
    month = d.month - 1 + months
    year, month = d.year + month // 12, month % 12 + 1
    return date(year, month, min(d.day, calendar.monthrange(year, month)[1]))


def src(sek: float) -> str:
    """A SEK amount as the source amount (SEK / factor), two decimals."""
    return f"{sek / FACTOR:.2f}"


def src_up(sek: float) -> str:
    """Rounded up, so the import's × factor never pays less than the amount due."""
    return f"{math.ceil(sek / FACTOR * 100) / 100:.2f}"


def annuity(principal: float, annual_rate: float, months: int) -> float:
    r = annual_rate / 12
    return round(principal * r * (1 + r) ** months / ((1 + r) ** months - 1), 2)


def due_payments(product: str, principal: float, start: date, term: int):
    """(due date, amount due in SEK) for each installment or stage, as Lending computes them (design plan section 3)."""
    rate = PRODUCTS[product]["rate"]
    if product != "PURCHASE":
        a = annuity(principal, rate, term)
        return [(add_months(start, k), a) for k in range(1, term + 1)]
    count = max(1, min(term // 3, PURCHASE_MAX_STAGES))
    share = math.floor(principal / count * 100) / 100
    outstanding, last, result = principal, start, []
    for i in range(1, count + 1):
        due = add_months(start, term * i // count)
        part = principal - share * (count - 1) if i == count else share
        interest = outstanding * rate * (due - last).days / 365  # Actual/365 from the last payment
        result.append((due, part + interest))
        outstanding, last = outstanding - part, due
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default="data/raw", help="output folder (default data/raw, gitignored)")
    parser.add_argument("--customers", type=int, default=100)
    parser.add_argument("--years", type=int, default=1, choices=[1, 2], help="history length; the import keeps at most 2")
    parser.add_argument("--seed", type=int, default=20260101)
    args = parser.parse_args()

    rnd = random.Random(args.seed)
    first_day = date(2024, 1, 1)
    last_day = first_day + timedelta(days=365 * args.years - 1)
    n = args.customers
    late_count, default_count, on_time_count, order_count = max(12, n // 8), 2, max(26, n * 3 // 10), max(18, n // 5)

    rows = {k: [] for k in ["customers", "accounts", "transactions", "loans", "loan_payments", "standing_orders"]}
    ids = {"T": 0, "P": 0}

    def next_id(prefix: str) -> str:
        ids[prefix] += 1
        return f"{prefix}{ids[prefix]:05d}"

    def tx(account: str, day: date, kind: str, sek: float, target: str = "") -> None:
        if first_day <= day <= last_day:
            rows["transactions"].append([next_id("T"), account, day.isoformat(), kind, src(sek), target])

    # Customers and their roles: defaulters, late borrowers, on-time borrowers, then everyday customers.
    roles = ["default"] * default_count + ["late"] * late_count + ["on_time"] * on_time_count
    roles += ["everyday"] * (n - len(roles))
    customers = []
    for i in range(1, n + 1):
        cid = f"C{i:03d}"
        first, last = rnd.choice(FIRST_NAMES), rnd.choice(LAST_NAMES)
        born = date(rnd.randint(1958, 2003), rnd.randint(1, 12), rnd.randint(1, 28))
        rows["customers"].append([cid, first, last, born.isoformat(), f"{first}.{last}.{i}@example.com".lower(),
                                  f"+46 70 000 {i // 100:02d} {i % 100:02d}", f"Example Street {i}"])
        role = roles[i - 1]
        current = f"A{i:03d}1"
        savings = f"A{i:03d}2" if role in ("late", "default") or rnd.random() < 0.4 else None
        rows["accounts"].append([current, cid, rnd.choice(["current", "checking"])])
        if savings:
            rows["accounts"].append([savings, cid, "savings"])
        customers.append({"id": cid, "role": role, "current": current, "savings": savings,
                          "salary": rnd.randrange(25_000, 65_000, 500), "payday": rnd.randint(20, 27)})

    everyday_currents = [c["current"] for c in customers if c["role"] in ("everyday", "on_time")]

    # Everyday money: opening deposit, monthly salary, card withdrawals, rent, transfers between accounts.
    for c in customers:
        borrower_only = c["role"] in ("late", "default")
        income = c["savings"] if borrower_only else c["current"]
        tx(income, first_day, "deposit", c["salary"])
        month = first_day.replace(day=1)
        while month <= last_day:
            pay = month.replace(day=c["payday"])
            tx(income, pay, "deposit", c["salary"])
            spend_from = c["savings"] if borrower_only else c["current"]
            budget = c["salary"] * rnd.uniform(0.35, 0.55)
            tx(spend_from, month + timedelta(days=rnd.randint(0, 4)), "transfer", round(c["salary"] * rnd.uniform(0.15, 0.25), -2), "EXT-RENT")
            for _ in range(rnd.randint(3, 7)):
                amount = round(min(budget, c["salary"] * rnd.uniform(0.02, 0.07)), -1)
                budget -= amount
                tx(spend_from, month + timedelta(days=rnd.randint(1, 27)), "withdrawal", amount)
            if not borrower_only and c["savings"] and rnd.random() < 0.7:
                tx(c["current"], pay + timedelta(days=1), "transfer", round(c["salary"] * rnd.uniform(0.05, 0.12), -2), c["savings"])
            if not borrower_only and rnd.random() < 0.15:
                tx(c["current"], month + timedelta(days=rnd.randint(5, 25)), "transfer", rnd.randrange(200, 2_000, 50), rnd.choice(everyday_currents))
            month = add_months(month, 1)

    # Standing orders, only from everyday and on-time customers (late borrowers' current accounts hold loan money only).
    order_owners = rnd.sample([c for c in customers if c["role"] in ("everyday", "on_time")], order_count)
    for k, c in enumerate(order_owners, start=1):
        start = first_day + timedelta(days=rnd.randint(10, 120))
        if c["savings"] and rnd.random() < 0.5:
            target, frequency, amount = c["savings"], "monthly", round(c["salary"] * 0.05, -2)
        else:
            target = rnd.choice([a for a in everyday_currents if a != c["current"]])
            frequency, amount = rnd.choice([("weekly", rnd.randrange(150, 500, 50)), ("monthly", rnd.randrange(500, 2_500, 100)),
                                            ("quarterly", rnd.randrange(1_000, 4_000, 250))])
        end = add_months(start, rnd.randint(4, 10)).isoformat() if rnd.random() < 0.3 else ""
        rows["standing_orders"].append([f"S{k:03d}", c["current"], target, src(amount), frequency, start.isoformat(), end])

    # Loans: the money leaves for the seller on the disbursement day, then the installments are paid into the account.
    borrowers = [c for c in customers if c["role"] in ("on_time", "late", "default")]
    for k, c in enumerate(borrowers, start=1):
        product = "CAR" if c["role"] == "default" else rnd.choices(["CAR", "MORTGAGE", "PURCHASE"], [5, 2, 4])[0]
        spec = PRODUCTS[product]
        latest_start = 60 if c["role"] == "default" else max(60, 365 * args.years - 120)
        start = first_day + timedelta(days=rnd.randint(14, latest_start))
        start = start.replace(day=min(start.day, 28))
        principal = float(rnd.randrange(spec["amount"][0], spec["amount"][1], 5_000 if product != "MORTGAGE" else 50_000))
        term = rnd.randint(*spec["term"])
        if product == "PURCHASE":
            term = max(spec["term"][0], term - term % 3)
        loan_id = f"L{k:03d}"
        rows["loans"].append([loan_id, c["id"], c["current"], rnd.choice(spec["types"]), src(principal), str(term), start.isoformat()])
        tx(c["current"], start, "transfer", principal, "EXT-SELLER")

        stop_after = last_day - timedelta(days=rnd.randint(110, 150)) if c["role"] == "default" else None
        late_marked = False
        for due, amount in due_payments(product, principal, start, term):
            if due > last_day or (stop_after and due > stop_after):
                break
            late = c["role"] in ("late", "default") and (rnd.random() < 0.4 or not late_marked)
            paid_on = due + timedelta(days=rnd.randint(6, 25)) if late else due - timedelta(days=rnd.randint(0, 2))
            if paid_on > last_day:
                break
            late_marked |= late
            # Above the amount due (1 %, or 3 % when late to cover the penalty), so an installment is never left short; the rest stays in the account.
            rows["loan_payments"].append([next_id("P"), loan_id, paid_on.isoformat(), src_up(amount * (1.03 if late else 1.01)), "1" if late else "0"])

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    headers = {
        "customers": ["id", "first_name", "last_name", "date_of_birth", "email", "phone", "address"],
        "accounts": ["id", "customer_id", "type"],
        "transactions": ["id", "account_id", "date", "kind", "amount", "target_account_id"],
        "loans": ["id", "customer_id", "account_id", "type", "amount", "term_months", "start_date"],
        "loan_payments": ["id", "loan_id", "date", "amount", "late"],
        "standing_orders": ["id", "account_id", "target_account_id", "amount", "frequency", "start_date", "end_date"],
    }
    for name, header in headers.items():
        if name == "transactions":
            rows[name].sort(key=lambda r: (r[2], r[0]))
        with open(out / f"{name}.csv", "w", newline="", encoding="utf-8") as f:
            writer = csv.writer(f, lineterminator="\n")
            writer.writerow(header)
            writer.writerows(rows[name])

    late_customers = {r[1] for r in rows["loans"] if any(p[1] == r[0] and p[4] == "1" for p in rows["loan_payments"])}
    print(f"Wrote {out.resolve()}: {len(rows['customers'])} customers, {len(rows['accounts'])} accounts, "
          f"{len(rows['transactions'])} transactions, {len(rows['loans'])} loans ({len(late_customers)} customers with late payments, "
          f"{default_count} who stop paying), {len(rows['loan_payments'])} loan payments, {len(rows['standing_orders'])} standing orders; "
          f"{first_day} to {last_day} before the date shift.")


if __name__ == "__main__":
    main()
