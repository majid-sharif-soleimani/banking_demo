using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.S3.Assets;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;

namespace Bank.Deploy;

public sealed class BankStackProps : StackProps
{
    /// <summary>The one network (CIDR) allowed to reach the gateway and SSH, such as Majid's own IP with /32.</summary>
    public required string AllowedCidr { get; init; }

    /// <summary>An existing EC2 key pair for SSH; empty means no SSH key (use Session Manager instead).</summary>
    public required string KeyPairName { get; init; }

    /// <summary>The GHCR image tag the instance runs.</summary>
    public required string ImageTag { get; init; }
}

/// <summary>
/// Everything the demo bank needs on AWS, and nothing more (cost-driven design, bank-platform ADR 0002):
/// <list type="bullet">
/// <item>a VPC with one public subnet and no NAT gateway;</item>
/// <item>one ARM t4g.medium instance that runs docker-compose.aws.yml;</item>
/// <item>a security group that lets one CIDR reach the gateway port and SSH;</item>
/// <item>an S3 bucket for uploaded files (files/) and nightly database backups (backups/);</item>
/// <item>one Secrets Manager secret for the passwords, whose value Majid sets (bank-platform ADR 0004);</item>
/// <item>an instance role that can use only that bucket and that secret.</item>
/// </list>
/// </summary>
public sealed class BankStack : Stack
{
    public const int GatewayPort = 8080;

    public BankStack(Construct scope, string id, BankStackProps props) : base(scope, id, props)
    {
        var vpc = new Vpc(this, "Vpc", new VpcProps
        {
            MaxAzs = 1,
            NatGateways = 0,
            SubnetConfiguration = [new SubnetConfiguration { Name = "public", SubnetType = SubnetType.PUBLIC, CidrMask = 24 }],
        });

        var firewall = new SecurityGroup(this, "Firewall", new SecurityGroupProps
        {
            Vpc = vpc,
            Description = "The gateway port and SSH, from one CIDR only",
            AllowAllOutbound = true,
        });
        firewall.AddIngressRule(Peer.Ipv4(props.AllowedCidr), Port.Tcp(GatewayPort), "Bank gateway");
        firewall.AddIngressRule(Peer.Ipv4(props.AllowedCidr), Port.Tcp(22), "SSH (tunnels to the dashboard, Mailpit, Kafka UI)");

        var bucket = new Bucket(this, "Files", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            Versioned = false,
            // Backups older than 30 days are deleted, so the bucket's cost stays bounded.
            LifecycleRules = [new LifecycleRule { Prefix = "backups/", Expiration = Duration.Days(30) }],
            // Customer documents and backups survive "cdk destroy"; Majid empties and deletes the bucket by hand.
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        // Created with a random placeholder value. Majid replaces it with the JSON of passwords (see deploy/README.md);
        // the value never appears in this code, the template or the repository.
        var secret = new Secret(this, "Passwords", new SecretProps
        {
            Description = "Demo bank passwords: POSTGRES_PASSWORD, <SERVICE>_DB_PASSWORD for six services, optional GHCR_TOKEN.",
        });

        var role = new Role(this, "InstanceRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("ec2.amazonaws.com"),
            Description = "The demo bank instance: its bucket, its secret, and Session Manager",
            ManagedPolicies = [ManagedPolicy.FromAwsManagedPolicyName("AmazonSSMManagedInstanceCore")],
        });
        bucket.GrantReadWrite(role);
        secret.GrantRead(role);

        // The files the instance needs, uploaded by "cdk deploy" to the CDK bootstrap bucket and fetched at boot.
        var compose = new Asset(this, "Compose", new AssetProps { Path = "../docker-compose.aws.yml" });
        var dbInit = new Asset(this, "DbInit", new AssetProps { Path = "../../db/init" });
        var scripts = new Asset(this, "Scripts", new AssetProps { Path = "../instance" });
        foreach (var asset in new[] { compose, dbInit, scripts })
        {
            asset.GrantRead(role);
        }

        var userData = UserData.ForLinux();
        userData.AddCommands(
            "set -euo pipefail",
            "dnf install -y docker jq unzip",
            "systemctl enable --now docker",
            // Compose v2 as a Docker CLI plugin (Amazon Linux's docker package has none). Check the version before deploying.
            "mkdir -p /usr/local/lib/docker/cli-plugins",
            "curl -fsSL https://github.com/docker/compose/releases/download/v2.29.7/docker-compose-linux-aarch64 -o /usr/local/lib/docker/cli-plugins/docker-compose",
            "chmod +x /usr/local/lib/docker/cli-plugins/docker-compose",
            "mkdir -p /opt/bank/deploy /opt/bank/db/init");
        userData.AddS3DownloadCommand(new S3DownloadOptions { Bucket = compose.Bucket, BucketKey = compose.S3ObjectKey, LocalFile = "/opt/bank/deploy/docker-compose.aws.yml" });
        var dbInitZip = userData.AddS3DownloadCommand(new S3DownloadOptions { Bucket = dbInit.Bucket, BucketKey = dbInit.S3ObjectKey, LocalFile = "/tmp/db-init.zip" });
        var scriptsZip = userData.AddS3DownloadCommand(new S3DownloadOptions { Bucket = scripts.Bucket, BucketKey = scripts.S3ObjectKey, LocalFile = "/tmp/scripts.zip" });
        userData.AddCommands(
            $"unzip -o {dbInitZip} -d /opt/bank/db/init",
            $"unzip -o {scriptsZip} -d /opt/bank/deploy",
            "chmod +x /opt/bank/deploy/*.sh",
            // Not secrets: where the secret and the bucket are. start-bank.sh reads the secret's value at each boot.
            $"printf 'SECRET_ID=%s\\nBANK_BUCKET=%s\\nAWS_REGION=%s\\nBANK_IMAGE_TAG=%s\\n' '{secret.SecretArn}' '{bucket.BucketName}' '{Aws.REGION}' '{props.ImageTag}' > /opt/bank/deploy/stack.env",
            "cp /opt/bank/deploy/bank.service /opt/bank/deploy/bank-backup.service /opt/bank/deploy/bank-backup.timer /etc/systemd/system/",
            "systemctl daemon-reload",
            "systemctl enable --now bank.service bank-backup.timer");

        var instance = new Instance_(this, "Bank", new InstanceProps
        {
            Vpc = vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
            InstanceType = InstanceType.Of(InstanceClass.T4G, InstanceSize.MEDIUM),
            MachineImage = MachineImage.LatestAmazonLinux2023(new AmazonLinux2023ImageSsmParameterProps { CpuType = AmazonLinuxCpuType.ARM_64 }),
            SecurityGroup = firewall,
            Role = role,
            UserData = userData,
            KeyPair = props.KeyPairName == "" ? null : KeyPair.FromKeyPairName(this, "Key", props.KeyPairName),
            // IMDSv2 only; hop limit 2 so the containers (one hop further) can use the instance role for S3.
            HttpTokens = HttpTokens.REQUIRED,
            HttpPutResponseHopLimit = 2,
            BlockDevices =
            [
                new BlockDevice
                {
                    DeviceName = "/dev/xvda",
                    // PostgreSQL and Kafka data live here and survive stop/start.
                    Volume = BlockDeviceVolume.Ebs(30, new EbsDeviceOptions { VolumeType = EbsDeviceVolumeType.GP3, Encrypted = true }),
                },
            ],
        });

        _ = new CfnOutput(this, "InstanceId", new CfnOutputProps { Value = instance.InstanceId, Description = "For start.sh, stop.sh and backup.sh" });
        _ = new CfnOutput(this, "PublicDns", new CfnOutputProps { Value = instance.InstancePublicDnsName, Description = $"Gateway at http://<this>:{GatewayPort}; changes after each stop/start" });
        _ = new CfnOutput(this, "BucketName", new CfnOutputProps { Value = bucket.BucketName });
        _ = new CfnOutput(this, "SecretName", new CfnOutputProps { Value = secret.SecretName, Description = "Set its value once (deploy/README.md)" });
    }
}
