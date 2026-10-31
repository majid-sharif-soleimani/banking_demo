#!/usr/bin/env bash
# Starts the demo instance and prints the gateway address (it changes after every stop/start).
# Run by Majid on his own machine, with his own AWS credentials. Usage: deploy/start.sh [stack-name]
set -euo pipefail
# The AWS CLI takes the region from AWS_REGION, which must be the stack's region.
: "${AWS_REGION:?Set AWS_REGION to the region of the stack, for example eu-north-1}"
stack=${1:-BankDemo}
output() { aws cloudformation describe-stacks --stack-name "$stack" --query "Stacks[0].Outputs[?OutputKey=='$1'].OutputValue" --output text; }
id=$(output InstanceId)
aws ec2 start-instances --instance-ids "$id" >/dev/null
aws ec2 wait instance-running --instance-ids "$id"
dns=$(aws ec2 describe-instances --instance-ids "$id" --query 'Reservations[0].Instances[0].PublicDnsName' --output text)
echo "Instance $id is running. The bank starts in a few minutes at http://$dns:8080"
echo "Dashboard, Mailpit, Kafka UI: ssh -L 18888:localhost:18888 -L 8025:localhost:8025 -L 8081:localhost:8081 ec2-user@$dns"
