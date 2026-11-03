# 0001. Match customer searches simply and add no index yet

- Status: Accepted
- Date: 2026-11-03
- Scope: bank-customers
- Design plan reference: none (phase 1 extension for the phase 2 agent); workspace ADRs 0004–0006
- Decided by: Claude on Majid's delegation of 2026-11-03.

## Context

Staff and the phase 2 agent must find a customer from what a caller says: part of a name, an email address, a phone number or a customer number. People type names partly ("Lovelace") and phone numbers in any format ("070-111 11 11", "+46 70 111 11 11"). The bank has about 100 customers today, and the demo will not grow beyond a few thousand. PostgreSQL offers several ways to match: exact equality, prefix `LIKE` (served by a B-tree), `ILIKE '%term%'` (no B-tree helps) and trigram search with the `pg_trgm` extension (a GIN index serves `ILIKE '%term%'`). The service roles have only `CONNECT` on the shared `bank` database (`bank-platform/db/init/001-roles-and-schemas.sql`), so they cannot run `CREATE EXTENSION`. There is also no separate "customer number" in the data model; the customer id is the only number the bank gives a customer.

## Decision

`POST /customers/search` accepts `name`, `email`, `phone` and `customerNumber`. Every given filter must match (AND), and at least one is required.

- **Name**: contains the term, case-insensitive: `name ILIKE '%term%'` with the term's `%`, `_` and `\` escaped (`Logic/CustomerSearch.ContainsPattern`). At least 2 characters.
- **Email**: equal, case-insensitive and trimmed: `lower(email) = lower(@email)`.
- **Phone**: equal on digits only: `regexp_replace(phone, '[^0-9]', '', 'g') = @digits`, so formatting does not matter. At least 5 digits. `+46 70…` and `070…` are different numbers; no country-code rule is invented.
- **Customer number**: the customer id, equal.
- **No new index.** The query reads the `customer` table sequentially.

`regexp_replace` is mapped as a database function on `CustomersDbContext` with an explicit `'g'` flag. Npgsql's translation of `Regex.Replace` only replaced the first match, which the integration tests caught.

## Rationale

- Exact matching for email, phone and number gives one answer, which is what identifying a caller needs. Contains matching for names finds "Lovelace" in "Ada Lovelace".
- A sequential scan of a few thousand narrow rows takes well under a millisecond. An index would cost writes and a migration and gain nothing at this size. ADR 0004 asks for an index matching each list's sort; here the whole match set is small, so sorting it is cheap.
- `pg_trgm` would be the right tool at scale. But it needs a superuser to run `CREATE EXTENSION` once in each environment (home, AWS, test containers), outside the service migrations. That is a new manual deployment step on a live bank, for no measurable gain today.
- Using the customer id as the customer number avoids inventing a business identifier (format, generation, uniqueness) that the design plan does not define.

## Alternatives considered

- `pg_trgm` with a GIN index on `name`: fast contains-search at any size, typo-tolerant similarity if wanted later. Rejected for now for the extension privilege and deployment step.
- Prefix match on `lower(name)` with a B-tree (`text_pattern_ops`): indexable without an extension, but it misses "Lovelace" in "Ada Lovelace" unless names are split into words.
- Expression indexes on `lower(email)` and on the phone digits: cheap, but not needed at this size; easy to add when the table grows.
- A new `customer_number` column (for example 8 digits plus a check digit): a real bank has one, but it is a business rule the design plan does not contain. Majid can add it as a separate decision.

## Consequences

- Searching is linear in the number of customers. At tens of thousands, add `pg_trgm` (with an init-script and deployment step) or the expression indexes. This ADR is then superseded.
- A phone stored with a country code does not match a search without it, and the reverse.
- The search returns a summary (id, name, email, phone, status), not the full record. The national id and address are only read through `GET /customers/{id}`, so a broad search exposes less.
