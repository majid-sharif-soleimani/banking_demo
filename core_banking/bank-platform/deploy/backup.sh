#!/usr/bin/env bash
# Takes a database backup now (the instance also takes one nightly), through Session Manager, so no SSH is needed.
# Usage: deploy/backup.sh [stack-name]
set -euo pipefail
stack=${1:-BankDemo}
id=$(aws cloudformation describe-stacks --stack-name "$stack" --query "Stacks[0].Outputs[?OutputKey=='InstanceId'].OutputValue" --output text)
command=$(aws ssm send-command --instance-ids "$id" --document-name AWS-RunShellScript \
  --parameters 'commands=["/opt/bank/deploy/backup-db.sh"]' --query Command.CommandId --output text)
aws ssm wait command-executed --command-id "$command" --instance-id "$id"
aws ssm get-command-invocation --command-id "$command" --instance-id "$id" --query StandardOutputContent --output text
