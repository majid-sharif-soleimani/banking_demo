using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bank.Contracts.Schema;
using Bank.Contracts.SchemaTool;
using Bank.Contracts.Tests.Support;
using Json.Schema;

namespace Bank.Contracts.Tests;

/// <summary>
/// The C# records are the source of truth; the schema files are snapshots generated from them (ADR 0003).
/// These tests fail when a record no longer matches its published snapshot, and check the snapshots themselves.
/// They only read; Bank.Contracts.SchemaTool writes the snapshots (ADR 0004).
/// </summary>
public class ContractTests
{
    private const string WriteCommand = "dotnet run --project tools/Bank.Contracts.SchemaTool -- write";

    public static TheoryData<string> EventTypes() =>
        new(ContractCatalog.EventTypes.Select(t => t.FullName!));

    public static TheoryData<string> SchemaUris() =>
        new(ContractSchemas.All.Select(k => k.ToString()));

    private static Type EventType(string fullName) => ContractCatalog.EventTypes.Single(t => t.FullName == fullName);

    [Fact]
    public void The_catalog_finds_the_stage_1_events()
    {
        ContractCatalog.EventTypes.Select(t => t.Name).ShouldBe(
            ["BusinessDayOpened", "CurrencyActivated", "DayClosed", "DayCompleted", "TestEvent"],
            ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void Every_event_record_declares_a_valid_SchemaUri_constant(string eventType)
    {
        var uri = ContractCatalog.SchemaUriOf(EventType(eventType));

        uri.ShouldNotBeNull($"{eventType} needs 'public const string SchemaUri'.");
        SchemaKey.TryParse(uri, out _).ShouldBeTrue($"{eventType}.SchemaUri '{uri}' is not a valid schema URI.");
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void Every_event_record_points_at_the_latest_version_of_its_schema(string eventType)
    {
        var key = SchemaKey.Parse(ContractCatalog.SchemaUriOf(EventType(eventType))!);

        var latest = ContractSchemas.All.Where(k => k.IsSameSchemaAs(key)).Max();

        key.ShouldBe(latest);
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void Every_event_record_matches_its_schema_snapshot(string eventType)
    {
        var type = EventType(eventType);
        var key = SchemaKey.Parse(ContractCatalog.SchemaUriOf(type)!);
        var generated = ContractSchemaGenerator.Generate(type, key);

        ContractSchemas.All.ShouldContain(key,
            $"No schema snapshot for {key}. Write it with '{WriteCommand}', then run 'dotnet test' again.");

        var published = ReadSchema(key);
        JsonNode.DeepEquals(published, generated).ShouldBeTrue(
            $"{type.Name} no longer matches the published schema {key}. A published version never changes: " +
            $"bump SchemaUri (ADDITION if you only added optional fields, otherwise a new MODEL) and run '{WriteCommand}'." +
            $"{Environment.NewLine}Published:{Environment.NewLine}{SchemaSnapshots.ToFileText(published)}" +
            $"Generated now:{Environment.NewLine}{SchemaSnapshots.ToFileText(generated)}");
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void A_serialized_event_validates_against_its_schema(string eventType)
    {
        var type = EventType(eventType);
        var key = SchemaKey.Parse(ContractCatalog.SchemaUriOf(type)!);
        var schema = JsonSchema.FromText(ContractSchemas.Read(key));
        var json = JsonSerializer.SerializeToElement(SampleFactory.Create(type), type, ContractJson.Options);

        var result = schema.Evaluate(json, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

        result.IsValid.ShouldBeTrue(string.Join(Environment.NewLine,
            (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))));
    }

    [Theory]
    [MemberData(nameof(SchemaUris))]
    public void Schema_path_and_self_agree(string schemaUri)
    {
        var key = SchemaKey.Parse(schemaUri);
        var self = ReadSchema(key)["self"]?.AsObject();

        self.ShouldNotBeNull($"{key.ResourcePath} has no 'self'.");
        self["vendor"]?.GetValue<string>().ShouldBe(key.Vendor);
        self["name"]?.GetValue<string>().ShouldBe(key.Name);
        self["format"]?.GetValue<string>().ShouldBe(key.Format);
        self["version"]?.GetValue<string>().ShouldBe(key.Version);
    }

    [Theory]
    [MemberData(nameof(SchemaUris))]
    public void Every_schema_is_an_object_with_the_common_event_fields(string schemaUri)
    {
        var schema = ReadSchema(SchemaKey.Parse(schemaUri));
        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

        schema["type"]!.GetValue<string>().ShouldBe("object");
        required.ShouldContain("eventId");
        required.ShouldContain("occurredOn");
        required.ShouldContain("performedBy");
    }

    [Fact]
    public void The_latest_version_of_every_schema_has_a_record()
    {
        var withRecord = ContractCatalog.EventTypes.Select(t => SchemaKey.Parse(ContractCatalog.SchemaUriOf(t)!)).ToHashSet();

        var latestVersions = ContractSchemas.All.GroupBy(k => (k.Vendor, k.Name, k.Format)).Select(g => g.Max()!);

        latestVersions.Where(k => !withRecord.Contains(k)).ShouldBeEmpty();
    }

    [Fact]
    public void Consecutive_schema_versions_are_compatible()
    {
        var problems = ContractSchemas.All
            .GroupBy(k => (k.Vendor, k.Name, k.Format))
            .SelectMany(g =>
            {
                var versions = g.Order().ToList();
                var firstVersionProblem = versions[0].Version == "1-0-0"
                    ? []
                    : new[] { $"{versions[0]}: the first version of a schema must be 1-0-0." };

                return firstVersionProblem.Concat(versions.Zip(versions.Skip(1),
                    (older, newer) => SchemaCompatibility.Check(older, ReadSchema(older), newer, ReadSchema(newer))).SelectMany(p => p));
            });

        problems.ShouldBeEmpty();
    }

    private static JsonObject ReadSchema(SchemaKey key) => JsonNode.Parse(ContractSchemas.Read(key))!.AsObject();
}
