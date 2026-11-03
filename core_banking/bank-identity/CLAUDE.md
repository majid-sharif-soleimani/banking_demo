# bank-identity

Logins and sessions for the agent's calls (workspace ADR 0007). No database, no Kafka, no events. The gateway routes `/api/identity/*` here and calls `GET /sessions/current` for every other `/api` request.

## Behaviour

- `POST /login {username, password}`: `admin`/`admin` (hard-coded in `Logic/SessionRules.cs`, test only) or a customer's national id as both username and password. Returns `token`, `username`, `role` (`admin` | `customer`), `customerId`, `name`, `expiresAt`. Wrong pair or unknown national id: 401 `invalid_credentials`. Customers unreachable: 503 `customers_unavailable`.
- `POST /logout` and `GET /sessions/current` read `Authorization: Bearer <token>`. 401 codes: `missing_token`, `invalid_token`, `session_expired`, `session_replaced` (said once, then `invalid_token`).
- One session per username: a new login replaces the old one. Idle timeout `Identity:IdleTimeout` (5 minutes), restarted by every valid request.
- Customer lookup: `Features/CustomerDirectory.cs`, `POST http://customers/customers/search {nationalId}` with `X-Performed-By: identity` (the one direct call between services). Never log the national id.

## Gotchas

- Sessions live in memory (`Sessions/SessionStore.cs`): a restart logs everyone out, and only one instance may run.
- Time is `TimeProvider` (technical time), not `IBusinessClock`. Tests replace it with `TestClock`.
- The ServiceDefaults resilience handler retries the Customers call; when Customers is down, the exception may be Polly's timeout, not `HttpRequestException`, so the login catches any exception.
- Tests start a stub Customers on a random port (`services:customers:http:0`), like the gateway tests.

## Commands

```bash
dotnet build && dotnet test      # no Docker needed
```
