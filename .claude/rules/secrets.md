# Secrets and personal data

Applies to every file in the workspace, in every stage.

## What counts as a secret or personal data

- Passwords, API keys and tokens (Claude, OpenAI, GitHub, GHCR, SMTP, SES), signing keys, certificates and private keys.
- Connection strings that contain a password or a non-local host (PostgreSQL, Kafka, S3, Mailpit/SES).
- AWS access keys, AWS account IDs, ARNs that contain an account ID, and real host names or IP addresses of deployed machines.
- Majid's own personal data: real name in config, email, phone, home address, personal URLs. Use placeholders such as `demo@example.com`.

When unsure, treat it as a secret.

## Where secrets live

Never in a tracked file. Never in code, `appsettings*.json`, `launchSettings.json`, Docker Compose files, CDK code, test code, ADRs, docs, commit messages or CI workflows. Only these places are allowed:

| Environment | Store |
| --- | --- |
| Local .NET services and the Aspire AppHost | `dotnet user-secrets` (stored outside the repo, in the user profile). Aspire parameters marked `secret: true` read from the AppHost's user secrets. |
| Local Docker Compose | A `.env` file next to the compose file, gitignored. Commit a `.env.example` with the same keys and placeholder values only. |
| Integration tests | Testcontainers credentials generated at runtime. Never a real secret. |
| AWS (EC2) | AWS Secrets Manager. The EC2 instance reads it through its IAM instance role; no access keys on the machine or in config. CDK creates the secret resource and grants read access, but never contains the secret value: Majid sets the value himself (console or CLI). |
| GitHub Actions | GitHub repository secrets (`${{ secrets.NAME }}`), set by Majid. |

How the AWS services load Secrets Manager values into .NET configuration (which configuration provider or package, caching and refresh) is a D1 decision for Majid with an ADR, made in the stage that first needs it.

## How code reads them

- Code reads secrets only through `IConfiguration` / options (`builder.Configuration.GetConnectionString("Accounts")`, `IOptions<T>`). It never knows which store the value came from, so local and AWS differ only in configuration.
- `appsettings.json` holds non-secret settings and the key names only (empty values or comments pointing to this rule). Environment variable overrides use the `__` form, for example `ConnectionStrings__Accounts`.
- A missing required secret fails at startup with a clear message naming the key and where to set it. No hard-coded fallback value.
- Never log, print, return in an API response or put in an exception message the value of a secret or a full connection string.

## .gitignore

The `.gitignore` must ignore at least:

```
.env
.env.*
!.env.example
secrets/
*.pem
*.key
*.pfx
appsettings.*.local.json
cdk.context.json
cdk.out/
```

## What Claude does

- Never ask Majid to paste a secret into the chat. If he does, do not repeat it, do not write it to any file, and tell him the command to store it himself (for example `dotnet user-secrets set "ConnectionStrings:Accounts" "<value>" --project <path>`).
- When a feature needs a new secret, add the key with a placeholder to `appsettings.json` (empty) and `.env.example`, list it in the service's README under "Secrets to set", and tell Majid which command sets it locally and which Secrets Manager secret holds it on AWS.
- Before every commit, check `git diff --cached` for secrets and personal data. If a file outside the allowed stores contains one, do not commit; move the value out first.
- If a secret has already been committed, stop and tell Majid. The fix is to rotate the secret first, then clean history only with his approval. Do not rewrite history on your own.
- Do not read `.env`, `secrets/`, key files or user-secrets, and do not run `dotnet user-secrets list` or `aws secretsmanager get-secret-value`; these are denied in `.claude/settings.json`. To know which keys exist, read `.env.example`, `appsettings.json` and the README.
