# Stage 8: End-to-end tests and AWS

Goal: a small set of end-to-end scenarios proves the whole system, and the same system can run on one EC2 instance on AWS.

Read first: design plan sections 8 and 9.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| The top of the test pyramid, infrastructure as code, container images and registries, CI/CD, cost-driven design | EC2 instance size and the start/stop routine; what runs in CI on every push and what runs only before a demo | End-to-end scenario 3 (missed installments through card payment); a review of the CDK stack with `/explain` before deployment. Majid runs every `cdk deploy`, `aws` command and image push himself |

Slices: (1) end-to-end scenarios, (2) docker-compose for AWS, (3) CDK stack, (4) S3 storage and scripts, (5) CI image jobs.

## End-to-end tests

New project `bank-platform/tests/Bank.EndToEndTests` using Aspire testing, all calls through the gateway:

1. Customer registers, opens SEK and EUR accounts, deposits, transfers SEK to EUR; ledger and statements agree.
2. Annuity loan: apply, review, request information, upload a document, approve, disburse, three months of collection, early payoff; trial balance zero.
3. Missed installments: overdue, penalties from day 6, manual card payment clears arrears, classification returns to Current.
4. 91 days unpaid: Default, emails sent.
5. Standing order fails for lack of funds; customer emailed; next run succeeds after a deposit.

## AWS

In `bank-platform/deploy/`:

- `docker-compose.aws.yml`: all services, gateway, PostgreSQL, Kafka, Mailpit, Aspire dashboard (standalone). Images from GHCR (`ghcr.io/<owner>/<service-folder>`).
- AWS CDK app in C# (`deploy/cdk`): one VPC with a public subnet, one ARM EC2 instance (t4g.medium), a security group exposing only the gateway port and SSH from a configurable CIDR, an S3 bucket for files and nightly PostgreSQL backups, an IAM role allowing S3 access. User data installs Docker and starts compose.
- `S3FileStorage` implementing `IFileStorage`, selected by configuration.
- Scripts: `deploy/start.sh`, `deploy/stop.sh` (start/stop the instance to save cost), `deploy/backup.sh`.
- Each service's CI workflow gains a job that builds and pushes its image to GHCR on `master`, disabled until Majid enables it.

Claude prepares everything but does not deploy: `cdk deploy`, `aws` commands and image pushes are Majid's. Note the estimated monthly cost from current AWS prices in the stage report.

## Done when

```bash
cd core_banking/bank-platform && dotnet test tests/Bank.EndToEndTests
cd core_banking/bank-platform/deploy/cdk && cdk synth      # succeeds without an AWS account
docker compose -f core_banking/bank-platform/deploy/docker-compose.aws.yml config    # valid
```
