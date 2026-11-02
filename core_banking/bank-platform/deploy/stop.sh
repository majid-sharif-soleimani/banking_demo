#!/usr/bin/env bash
# Stops the demo instance: compute billing stops, the EBS volume (and its data) stays. Usage: deploy/stop.sh [stack-name]
set -euo pipefail
# The AWS CLI takes the region from AWS_REGION, which must be the stack's region.
: "${AWS_REGION:?Set AWS_REGION to the region of the stack, for example eu-north-1}"
stack=${1:-BankDemo}
id=$(aws cloudformation describe-stacks --stack-name "$stack" --query "Stacks[0].Outputs[?OutputKey=='InstanceId'].OutputValue" --output text)
aws ec2 stop-instances --instance-ids "$id" >/dev/null
aws ec2 wait instance-stopped --instance-ids "$id"
echo "Instance $id is stopped."
