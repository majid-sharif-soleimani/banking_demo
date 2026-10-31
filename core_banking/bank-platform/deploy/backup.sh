#!/usr/bin/env bash
# Takes a database backup now (the instance also takes one nightly), through Session Manager, so no SSH is needed.
# Usage: deploy/backup.sh [stack-name]
set -euo pipefail
# The AWS CLI takes the region from AWS_REGION, which must be the stack's region.
: "${AWS_REGION:?Set AWS_REGION to the region of the stack, for example eu-north-1}"
stack=${1:-BankDemo}
id=$(aws cloudformation describe-stacks --stack-name "$stack" --query "Stacks[0].Outputs[?OutputKey=='InstanceId'].OutputValue" --output text)
command=$(aws ssm send-command --instance-ids "$id" --document-name AWS-RunShellScript \
  --parameters 'commands=["/opt/bank/deploy/backup-db.sh"]' --query Command.CommandId --output text)
# The waiter fails when the command fails (or after about 100 s); show what happened either way.
aws ssm wait command-executed --command-id "$command" --instance-id "$id" || true
aws ssm get-command-invocation --command-id "$command" --instance-id "$id" \
  --query '[Status, StandardOutputContent, StandardErrorContent]' --output text
