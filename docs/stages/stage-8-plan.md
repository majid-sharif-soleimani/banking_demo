# Stage 8 plan: End-to-end tests and AWS

## Context

Stages 2–7 are Ready for review (not merged). Stage 8 does two things.

- **It proves the whole system** with five end-to-end scenarios. They run against the real AppHost (six services, the gateway, PostgreSQL, Kafka, Mailpit), and every call goes through the gateway. Unit and service tests prove each part; only these scenarios prove that the parts agree. For example, the ledger and the statements must tell the same story, and a missed installment must end with an email.
- **It prepares the same system for one small EC2 instance** that runs only during demos (design plan section 9). The pieces are a Compose file with the services, a CDK stack, S3 for files and backups, start, stop and backup scripts, and CI jobs that build images.

The hard parts:

- **Time.** The scenarios cover months of business days. Each scenario starts a fresh bank on 2026-01-01 and closes days one by one, waiting for every end of day.
- **Eventual consistency.** Collections, allocations and journal entries arrive through Kafka after a day opens, so every check waits until the state settles.
- **Nothing on AWS happens here.** Claude writes the code; `cdk deploy`, every `aws` command and every image push are Majid's (stage file, workspace rules). `cdk synth` must work without an AWS account.

Environment: `~/repos/banking_demo` on `<machine>`, branch `stage-8-e2e-aws` (from `stage-7-gateway-import`).

**Working mode (Majid, 2026-10-31):** Claude writes everything, including the "Majid writes" item (scenario 3), with a learning document per slice. D1 decisions are made by Claude under Majid's delegation and listed below. The CDK review with `/explain` before deployment stays Majid's, and so do all deployments.

## Slices

1. **End-to-end scenarios** in `bank-platform/tests/Bank.EndToEndTests` (Aspire testing, one fresh AppHost per scenario, scenarios run one after another):
   1. Customer registers, opens SEK and EUR accounts, deposits, transfers SEK to EUR; ledger and statements agree.
   2. Annuity loan: apply, review, request information, upload a document, approve, disburse, three months of collection, early payoff; trial balance zero.
   3. Missed installments: overdue, penalties from day 6, manual card payment clears arrears, classification returns to Current.
   4. 91 days unpaid: Default, emails sent.
   5. Standing order fails for lack of funds; customer emailed; next run succeeds after a deposit.
2. **Docker Compose for AWS:** `bank-platform/deploy/docker-compose.aws.yml` with all services (GHCR images), the gateway, PostgreSQL, Kafka, Mailpit and the standalone Aspire dashboard. Only the gateway port is published to the outside.
3. **CDK stack** in C# (`deploy/cdk`):
   - one VPC with a public subnet;
   - one t4g.medium instance;
   - a security group that opens the gateway port and SSH from a configurable CIDR;
   - an S3 bucket for files and backups;
   - an IAM role with access to that bucket and to one Secrets Manager secret;
   - user data that installs Docker and starts Compose.
4. **S3 storage and scripts:**
   - `S3FileStorage : IFileStorage` in Customers, selected by `Files:Provider`;
   - `deploy/start.sh`, `deploy/stop.sh` and `deploy/backup.sh`.
5. **CI image jobs:**
   - each service workflow gets a job that builds its image and pushes it to GHCR on `master`, disabled until Majid enables it;
   - an end-to-end workflow that runs only on demand.

## Decisions to review (Majid's delegation of 2026-10-31)

| Decision (D1) | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| EC2 instance size and start/stop routine | bank-platform 0002 | t4g.medium (2 vCPU, 4 GB, ARM), as the stage file says. Started and stopped by hand with `start.sh` and `stop.sh`; no schedule. The EBS volume keeps the data between demos | A schedule (EventBridge) that stops the instance every night: safer against a forgotten instance, but more moving parts |
| What runs in CI on every push and what only before a demo | bank-platform 0003 | Every push runs each service's unit and service integration tests, as today. The end-to-end scenarios (minutes, the whole AppHost) run only on demand (`workflow_dispatch`), before a demo or a merge | End-to-end on every push to `master`: earlier warning, but slow and costly in CI minutes |
| How the AWS instance gets its secrets (`.claude/rules/secrets.md` asks for an ADR in the first stage that needs it) | bank-platform 0004 | One Secrets Manager secret (JSON) created empty by CDK. At boot, `start-bank.sh` on the instance reads it with the instance role and writes a root-only `.env` that Compose reads. The services keep reading `IConfiguration` (environment variables) and never know about AWS | A .NET Secrets Manager configuration provider in every service: no file on disk, but an AWS dependency in every service |

Interpretations:

- **The bucket holds files under `files/` and backups under `backups/`.** Backups expire after 30 days (lifecycle rule) to bound the cost.
- **The standalone Aspire dashboard is only reachable through an SSH tunnel**, like Mailpit and Kafka UI. Only the gateway port is open.
- **The images are built with the .NET SDK container support** (`dotnet publish /t:PublishContainer`), with no Dockerfiles, for linux-arm64 to match the instance.
- **The end-to-end scenarios check the books more directly than the trial balance alone.** Customer deposits in the ledger must equal the account balances, and loans receivable must be zero after a payoff.

## Done when

```bash
cd core_banking/bank-platform && dotnet test tests/Bank.EndToEndTests
cd core_banking/bank-platform/deploy/cdk && cdk synth      # succeeds without an AWS account
docker compose -f core_banking/bank-platform/deploy/docker-compose.aws.yml config    # valid
```
