#!/usr/bin/env bash
# Stops the demo instance: compute billing stops, the EBS volume (and its data) stays. Usage: deploy/stop.sh [stack-name]
set -euo pipefail
stack=${1:-BankDemo}
id=$(aws cloudformation describe-stacks --stack-name "$stack" --query "Stacks[0].Outputs[?OutputKey=='InstanceId'].OutputValue" --output text)
aws ec2 stop-instances --instance-ids "$id" >/dev/null
aws ec2 wait instance-stopped --instance-ids "$id"
echo "Instance $id is stopped."
