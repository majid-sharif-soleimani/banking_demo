# 0004. The instance reads Secrets Manager into one env file per container

- Status: Accepted (reviewed and confirmed by Majid on 2026-11-02)
- Date: 2026-10-31
- Scope: bank-platform (deploy/cdk, deploy/instance/start-bank.sh, deploy/docker-compose.aws.yml)
- Design plan reference: section 9; `.claude/rules/secrets.md` ("How the AWS services load Secrets Manager values into .NET configuration is a D1 decision ... in the stage that first needs it")
- Decided by: Claude on Majid's delegation of 2026-10-31

## Context

On AWS the passwords (PostgreSQL admin, six service roles, optionally a GHCR token) live in AWS Secrets Manager (secrets rule). The services read configuration only through `IConfiguration`, and locally they get it from Aspire. Something must bring the values from Secrets Manager into the containers without writing them into the repository, the CDK template or the compose file, and without giving one service another service's password.

## Decision

- CDK creates **one secret** with a random placeholder and grants the instance role read access. Majid sets its value once, as JSON with the keys `POSTGRES_PASSWORD`, `OPERATIONS_DB_PASSWORD`, `CUSTOMERS_DB_PASSWORD`, `ACCOUNTS_DB_PASSWORD`, `CARDS_DB_PASSWORD`, `LENDING_DB_PASSWORD`, `LEDGER_DB_PASSWORD`, and optionally `GHCR_TOKEN` and `GHCR_USER`.
- At every boot, `start-bank.sh` (a systemd service on the instance) reads the secret with the instance role through the AWS CLI. It then writes **one env file per container** in `/opt/bank/deploy/env/`, readable by root only:
  - `postgres.env` gets all the passwords, because the init script creates the roles;
  - `<service>.env` gets only that service's connection string.
- `docker-compose.aws.yml` loads each container's file with `env_file` (optional, so `docker compose config` is valid without them). The services see ordinary `ConnectionStrings__<service>-db` environment variables, exactly the keys Aspire sets locally.

## Rationale

- The services stay unaware of AWS: local and AWS differ only in where the environment variables come from (secrets rule: "local and AWS differ only in configuration").
- Per-container files keep the database boundary of ADR 0001 for the environment: a service sees only its own password.
- **The instance role is the other way to the secret.** Any container that reaches the instance metadata service gets the role's credentials. So `start-bank.sh` blocks the metadata address (169.254.169.254) for all containers in Docker's `DOCKER-USER` chain, except Customers. Customers has a fixed address on the `bank` network and needs the role for S3. The role itself can only read and write `files/*`, put new objects under `backups/*` (never read or delete them), and read this one secret.
- Reading at boot means a rotated password takes effect at the next restart with no redeploy.

## Alternatives considered

- **A Secrets Manager configuration provider in every service** (for example `Kralizek.Extensions.Configuration.AWSSecretsManager`): no file on disk, but an AWS package and AWS-specific code in six services, and every service could read the whole secret.
- **One secret per service:** cleaner least privilege inside AWS, but seven secrets at $0.40 each per month and seven values for Majid to set, for a demo.
- **Docker secrets (files under /run/secrets):** the services would need code to read files instead of environment variables.

## Consequences

- The passwords exist in plain text on the instance's encrypted EBS volume, readable by root. Acceptable for a demo instance that only Majid can reach.
- **Remaining risk:** a compromised Customers container can use the role and so read the whole secret. Closing that needs a role per container (ECS task roles, or a credentials sidecar), which is beyond one EC2 instance. It is accepted for the demo and noted here.
- The values must be letters and digits, at least 24 characters, because they go into env files and connection strings.
- If a key is missing, empty or malformed, `start-bank.sh` stops with the key's name before writing any file, so the database never creates a role with an empty password. (The stage 8 review found that the first version wrote empty values instead.)
