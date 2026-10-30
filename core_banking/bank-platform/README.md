# bank-platform

Runs the infrastructure every bank service needs, the same way on a laptop and on AWS: one PostgreSQL database where each service can only reach its own schema, Kafka for events, Kafka UI to look at topics, and Mailpit to catch emails.

- Locally: `dotnet run --project src/Bank.AppHost` starts everything with .NET Aspire and opens the dashboard.
- Without Aspire (EC2, stage 8): `docker compose up -d --wait` with a filled-in `.env`.

How the database boundary works and why: `docs/adr/0001-one-role-and-schema-per-service.md`. The full explanation of this folder: `docs/learning/stage-1/slice-1.4.md` in the workspace root.

## Secrets to set

| Key | AppHost (user secrets) | Docker Compose (`.env`) | AWS (stage 8) |
| --- | --- | --- | --- |
| PostgreSQL admin password | `Parameters:postgres-password` (generated if not set) | `POSTGRES_PASSWORD` | Secrets Manager, decided in stage 8 |
| One password per service role | `Parameters:<service>-db-password` (generated if not set) | `<SERVICE>_DB_PASSWORD` | Secrets Manager, decided in stage 8 |

None needs to be set to run the AppHost: it generates them. To choose one:

```bash
dotnet user-secrets set "Parameters:operations-db-password" "<value>" --project src/Bank.AppHost
```

For Compose, copy `.env.example` to `.env` (gitignored) and replace every placeholder. Compose refuses to start while a key is missing.
