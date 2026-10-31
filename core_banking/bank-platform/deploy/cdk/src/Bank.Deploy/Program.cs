using Amazon.CDK;
using Bank.Deploy;

// The AWS app for the demo bank (design plan section 9, stage 8). "cdk synth" works without an AWS account: the stack
// names no account and looks nothing up. Majid runs "cdk bootstrap" and "cdk deploy", with his own IP and a pinned
// AMI given on the command line or in the gitignored cdk.context.json, never in cdk.json (deploy/README.md).
var app = new App();
var region = (string)app.Node.TryGetContext("bank:region") ?? "eu-north-1";

_ = new BankStack(app, "BankDemo", new BankStackProps
{
    Description = "Demo core bank on one EC2 instance (Docker Compose), with S3 for files and backups.",
    // Only the region: the account comes from Majid's credentials at deploy time and is never written down.
    Env = new Amazon.CDK.Environment { Region = region },
    AllowedCidr = (string)app.Node.TryGetContext("bank:allowedCidr") ?? "203.0.113.10/32",
    KeyPairName = (string)app.Node.TryGetContext("bank:keyPairName") ?? "",
    ImageTag = (string)app.Node.TryGetContext("bank:imageTag") ?? "latest",
    AmiId = (string)app.Node.TryGetContext("bank:amiId") ?? "",
});

app.Synth();
