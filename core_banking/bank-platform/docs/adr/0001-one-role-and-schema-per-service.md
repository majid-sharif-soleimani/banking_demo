# 0001. One database role and one schema per service

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-platform (PostgreSQL init script, AppHost, Docker Compose), used by every service
- Design plan reference: section 4, ADR rows 03, 04 and 11; `docs/implementation-decisions.md`, Database

## Context

Six services own their data, and no service may read another's (design plan ADR 04: services talk only through events). For cost, they share one PostgreSQL server and one database `bank` (ADR 11). If every service connected as the same user, the boundary would be a convention only: one wrong `JOIN` against another service's table would work, pass the tests, and quietly couple two services. We want the database to refuse it.

The passwords of these roles are secrets. They must not appear in the init script, the AppHost code or the compose file (`.claude/rules/secrets.md`), yet the same script must work under Aspire, under Docker Compose on EC2, and in the tests.

## Decision

1. **One login role and one schema per service**, with the same name: `operations`, `customers`, `accounts`, `cards`, `lending`, `ledger`. `db/init/001-roles-and-schemas.sql` creates them when the PostgreSQL container starts on an empty data directory, in the database `POSTGRES_DB=bank`.
2. **Each role owns its schema** (`CREATE SCHEMA x AUTHORIZATION x`), so it can create, change and drop its own tables and run its own EF Core migrations. Its `search_path` is its schema, so EF Core models and migrations name no schema.
3. **Nothing is shared by default.** The script revokes all rights on the database and on `public` from `PUBLIC`, then grants each role only `CONNECT` on `bank`. A role has no `USAGE` on any other schema, so it cannot read, write or even see the tables there, and it cannot create tables in `public` or new schemas.
4. **Passwords come from the environment.** The script reads `<SERVICE>_DB_PASSWORD` with psql `\getenv`, checks all six before creating anything, and stops with an error naming the missing one.
   - AppHost: one secret parameter `<service>-db-password` per service, generated when not set in the user secrets, passed to the container as that variable. The AppHost also builds the connection string `<service>-db` (host, port, `bank`, role, password) that a service gets with `WithReference` in later stages.
   - Docker Compose: the same variables from the gitignored `.env`; `${VAR:?}` makes Compose refuse to start when one is missing.
5. The admin user (`postgres`) is used only by the platform itself and the tests, never by a service.

## Rationale

- The boundary is enforced by PostgreSQL permissions, so a mistake fails at once with `42501 permission denied` instead of working by accident. `Bank.Platform.IntegrationTests` checks this for every pair of services.
- Owning the schema is the smallest grant that lets a service run its own migrations. No per-table grants to keep in sync.
- One script for every environment: Aspire, Compose and the tests run the same file, so what is tested is what runs on EC2.
- Reading the passwords from the environment keeps the script free of secrets and lets each environment use its own store.

## Alternatives considered

- One user for all services, schemas by convention: rejected. The simplest setup, but nothing stops a cross-schema query, which is exactly the coupling ADR 04 forbids.
- A database per service: rejected by design plan ADR 11 for cost and operations on one small EC2 instance. It would give an even stronger boundary (no cross-database queries at all in PostgreSQL).
- Roles created by each service at startup with the admin user: rejected. Every service would need the admin password, which defeats least privilege.
- Passwords in the script, or `md5` hashes of them: rejected. A hash in a tracked file is still a credential that can be used or cracked.

## Consequences

- The script runs only on an empty data directory. Adding a service or changing a role later means a new migration-like script (`002-...sql`) for existing volumes, or recreating the volume locally (`docker compose down -v` deletes all data).
- With Compose, a failed init leaves an initialized data volume without all roles; the next start skips the script. Compose's `${VAR:?}` checks make this unlikely; if it happens, run `docker compose down -v`.
- The AppHost has no data volume: every run starts with an empty database and new generated passwords. Data does not survive a restart. Stage 7 (data import) may want a volume; then the passwords must be fixed in the user secrets.
- Reports that need data from several services cannot join across schemas. They are built from events (Ledger), as the design plan intends.
- Adding a service means changing three places: the init script, `BankDatabase.Services` in the AppHost and `.env.example` (plus the test list).
