# 0002. One t4g.medium instance, started and stopped by hand

- Status: Accepted (reviewed and confirmed by Majid on 2026-11-02)
- Date: 2026-10-31
- Scope: bank-platform (deploy/cdk, deploy/start.sh, deploy/stop.sh)
- Design plan reference: section 9 (one small EC2 instance that runs only during demos); stage 8 file (D1: instance size and start/stop routine)
- Decided by: Claude on Majid's delegation of 2026-10-31

## Context

The bank runs on AWS only for demos. The whole system (six services, the gateway, PostgreSQL, Kafka, Mailpit and the Aspire dashboard) must fit on one machine. The bill should be a few dollars a month, not 30 to 50. The biggest cost is compute time, so the questions are how big the instance is and who makes sure it is not left running.

## Decision

- **One t4g.medium** (2 ARM vCPUs, 4 GB RAM), as the stage file and the design plan suggest. Kafka's heap is capped at 512 MB.
- **The data survives a stop, and a replacement.** The root volume is 30 GB of gp3 (encrypted) and holds PostgreSQL's and Kafka's data volumes (Kafka's log directory is set to its volume explicitly). The volume is kept even if the instance is terminated (`DeleteOnTermination = false`). The AMI is **pinned** (`bank:amiId`), because a changed image id makes CloudFormation replace the instance on the next deploy.
- **Started and stopped by hand:** `deploy/start.sh` starts the instance and prints the new address, and `deploy/stop.sh` stops it. Majid runs both with his own credentials. No schedule.
- **No Elastic IP:** the public address changes at every start, and `start.sh` prints it.

## Rationale

- Seven .NET services and the dashboard at about 150–250 MB each, PostgreSQL, Kafka (512 MB heap) and Kafka UI (a JVM, about 250 MB) need about 3–3.5 GB. 4 GB is tight but enough; 2 GB (t4g.small) would not start. If memory runs short, drop Kafka UI on AWS first. ARM (Graviton) costs about 20 % less than the same x86 size.
- A demo is a planned event, so starting by hand costs one command. A forgotten instance costs about a dollar a day, which is visible on the next bill and not dangerous.
- An Elastic IP is charged while the instance is stopped, which is exactly the instance's normal state.

## Alternatives considered

- **A nightly stop schedule (EventBridge plus a Lambda or SSM automation):** safer against forgetting, but more resources to understand, test and pay for. A later addition if a forgotten instance ever happens.
- **t4g.large (8 GB):** comfortable, and twice the price per hour.
- **ECS Fargate or EKS:** proper for production and far beyond the budget and the learning goal of this stage.

## Consequences

- Majid must remember `stop.sh` after a demo. The stage report gives the cost of forgetting.
- The address changes at every start; bookmarks need the new one.
- The bank needs a few minutes after `start.sh` (pull, migrations, Kafka) before the gateway answers.
- Upgrading the operating system is a deliberate step: take a backup, deploy with a new `bank:amiId`, restore if needed.
- These points were added after the stage 8 review, which found the unpinned AMI and Kafka's data outside its volume.
