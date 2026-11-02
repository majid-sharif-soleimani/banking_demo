# 0003. Service tests on every push, end-to-end on every push to master, images on master

- Status: Accepted (decided by Majid on 2026-11-02)
- Date: 2026-10-31
- Scope: `.github/workflows/` (all service workflows, `bank-e2e.yml`)
- Design plan reference: section 8 (test levels), section 9 (images from GitHub Actions to GHCR); stage 8 file (D1: what runs in CI on every push and what only before a demo)
- Decided by: Majid on 2026-11-02 (a D1 in the stage 8 file). Claude's first proposal, end-to-end only on demand, was replaced in his review.

## Context

There are now four test levels: unit, service integration (Testcontainers), contract and end-to-end. The end-to-end scenarios start the whole AppHost five times and close hundreds of business days, which takes several minutes. Images for AWS must be built somewhere and pushed to GHCR. Nothing may be published before Majid decides to.

## Decision

- **Every push** runs each service's own workflow: build, unit tests and service integration tests, as before. Path filters keep a change in one service from running everyone's tests.
- **The end-to-end scenarios run on every push to `master`** (`bank-e2e.yml`), and on demand (`workflow_dispatch`), for example on a stage branch before it is merged. A newer push to `master` cancels the run for the older commit (`concurrency`). When a scenario fails, the services' logs are kept as an artifact.
- **Images:** every service workflow and the gateway workflow have an `image` job. It builds a linux-arm64 image with the .NET SDK's container support (no Dockerfile) and pushes it to `ghcr.io/<owner>/bank-<service>`, tagged `latest` and the commit SHA. It runs only on a push to `master`, after the tests pass, and only when the repository variable `PUBLISH_IMAGES` is `true`. Until Majid sets that variable, the job is skipped.

## Rationale

- Service tests catch almost every defect in seconds to a minute, close to the change. The end-to-end scenarios catch the rest, the disagreements between services. Running them on every push to `master` means a broken `master` is known within minutes, without anyone having to remember a manual run.
- A variable instead of a commented-out job keeps the workflow valid and tested by GitHub, and enabling it is one setting with no code change.

## Alternatives considered

- **End-to-end only on demand** (Claude's first proposal): the fewest CI minutes, but a cross-service regression is found only when someone remembers to run them. Replaced by Majid on 2026-11-02.
- **End-to-end nightly on a schedule:** no wait on a push, but a failure is seen the next morning and the run happens even when nothing changed. Not chosen.
- **Dockerfiles per service:** more control, but one more file per service to keep in step with the SDK. The SDK builds the same image from the project.

## Consequences

- Each push to `master` costs about 5 minutes of runner time for the scenarios, on top of the service workflows. On a public repository GitHub-hosted runners are free; on a private one this counts against the monthly minutes.
- Images built on an x64 runner for arm64 are not run in CI; the first run on the instance is their first start.
