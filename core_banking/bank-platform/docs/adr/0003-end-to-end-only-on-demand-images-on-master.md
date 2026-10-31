# 0003. Service tests on every push, end-to-end on demand, images on master

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: `.github/workflows/` (all service workflows, `bank-e2e.yml`)
- Design plan reference: section 8 (test levels), section 9 (images from GitHub Actions to GHCR); stage 8 file (D1: what runs in CI on every push and what only before a demo)
- Decided by: Claude on Majid's delegation of 2026-10-31

## Context

There are now four test levels: unit, service integration (Testcontainers), contract and end-to-end. The end-to-end scenarios start the whole AppHost five times and close hundreds of business days, which takes several minutes. Images for AWS must be built somewhere and pushed to GHCR. Nothing may be published before Majid decides to.

## Decision

- **Every push** runs each service's own workflow: build, unit tests and service integration tests, as before. Path filters keep a change in one service from running everyone's tests.
- **The end-to-end scenarios run only on demand** (`bank-e2e.yml`, `workflow_dispatch`): before a demo, and before a stage branch is merged into `master`. When a scenario fails, the services' logs are kept as an artifact.
- **Images:** every service workflow and the gateway workflow have an `image` job. It builds a linux-arm64 image with the .NET SDK's container support (no Dockerfile) and pushes it to `ghcr.io/<owner>/bank-<service>`, tagged `latest` and the commit SHA. It runs only on a push to `master`, after the tests pass, and only when the repository variable `PUBLISH_IMAGES` is `true`. Until Majid sets that variable, the job is skipped.

## Rationale

- Service tests catch almost every defect in seconds to a minute, close to the change. The end-to-end scenarios catch the rest, the disagreements between services, and they matter at the moments when the whole bank is shown or merged.
- A variable instead of a commented-out job keeps the workflow valid and tested by GitHub, and enabling it is one setting with no code change.

## Alternatives considered

- **End-to-end on every push to `master`:** earlier warning, but `master` only changes when a stage is merged, which is exactly when the scenarios are run by hand anyway.
- **Dockerfiles per service:** more control, but one more file per service to keep in step with the SDK. The SDK builds the same image from the project.

## Consequences

- A cross-service regression between two demos is found when the scenarios are next run by hand, not at the push.
- Images built on an x64 runner for arm64 are not run in CI; the first run on the instance is their first start.
