using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace MediaMan.Server.Tests.Conformance;

/// <summary>
/// <c>contracts/openapi.yaml</c> loaded for tests: finds operations and validates response bodies against
/// their schemas (SPEC §12 API conformance). OpenAPI 3.1 schemas are JSON Schema 2020-12.
/// </summary>
public sealed class ContractDocument
{
    private const string ComponentSchemaRef = "#/components/schemas/";

    private static readonly Lazy<ContractDocument> _loaded = new(() =>
        new ContractDocument(Path.Combine(AppContext.BaseDirectory, "contracts", "openapi.yaml")));

    private static readonly string[] _httpMethods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private ContractDocument(string path)
    {
        var yaml = new YamlStream();
        using (var reader = File.OpenText(path))
        {
            yaml.Load(reader);
        }

        Root = (JsonObject)ToJson(yaml.Documents[0].RootNode)!;
    }

    public static ContractDocument Instance => _loaded.Value;

    public JsonObject Root { get; }

    public IEnumerable<ContractOperation> Operations =>
        from path in Root["paths"]!.AsObject()
        from method in path.Value!.AsObject()
        where _httpMethods.Contains(method.Key)
        select new ContractOperation(path.Key, method.Key.ToUpperInvariant(), method.Value!.AsObject());

    /// <summary>The operation whose path template matches <paramref name="requestPath"/>, or null.</summary>
    public ContractOperation? Find(string method, string requestPath) =>
        Operations.FirstOrDefault(o =>
            string.Equals(o.Method, method, StringComparison.OrdinalIgnoreCase) && TemplateMatches(o.Path, requestPath));

    /// <summary>A schema that is just a reference to a named component schema.</summary>
    public static JsonNode ComponentSchema(string name) => new JsonObject { ["$ref"] = ComponentSchemaRef + name };

    /// <summary>The schema the contract declares for <paramref name="status"/> and <paramref name="mediaType"/>, falling back to <c>default</c>.</summary>
    public JsonNode ResponseSchema(ContractOperation operation, int status, string mediaType)
    {
        var responses = operation.Node["responses"]!.AsObject();
        var response = responses[status.ToString(CultureInfo.InvariantCulture)] ?? responses["default"]
            ?? throw new InvalidOperationException($"{operation} declares no {status} or default response.");
        response = Resolve(response);

        var content = response["content"]?[mediaType]
            ?? throw new InvalidOperationException($"{operation} response {status} declares no '{mediaType}' content.");
        return content["schema"] ?? throw new InvalidOperationException($"{operation} response {status} '{mediaType}' has no schema.");
    }

    /// <summary>Validates <paramref name="instance"/> against <paramref name="schema"/>; returns the errors, empty when valid.</summary>
    public IReadOnlyList<string> Validate(JsonNode schema, JsonElement instance)
    {
        var document = RewriteRefs(schema.DeepClone()).AsObject();
        document["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        document["$defs"] = RewriteRefs(Root["components"]!["schemas"]!.DeepClone());

        var built = JsonSchema.FromText(document.ToJsonString(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        var results = built.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
        {
            return [];
        }

        return [.. (results.Details ?? [results])
            .Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))];
    }

    /// <summary>Checks status, content type and body of <paramref name="response"/> against the contract.</summary>
    public async Task AssertConformsAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var request = response.RequestMessage!;
        var operation = Find(request.Method.Method, request.RequestUri!.AbsolutePath);
        operation.Should().NotBeNull($"{request.Method} {request.RequestUri.AbsolutePath} must be in the contract");

        var declared = operation!.Node["responses"]![((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)];
        if (declared is not null && Resolve(declared)["content"] is null)
        {
            (await response.Content.ReadAsByteArrayAsync(ct)).Should().BeEmpty($"the contract declares no body for {(int)response.StatusCode} of {operation}");
            return;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        mediaType.Should().NotBeNull("every response declares a content type");

        var schema = ResponseSchema(operation!, (int)response.StatusCode, mediaType!);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Validate(schema, body.RootElement).Should().BeEmpty($"the {(int)response.StatusCode} body of {operation} must match the contract");
    }

    private JsonNode Resolve(JsonNode node)
    {
        if (node["$ref"]?.GetValue<string>() is not { } reference)
        {
            return node;
        }

        JsonNode current = Root;
        foreach (var segment in reference.TrimStart('#', '/').Split('/'))
        {
            current = current[segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)]
                ?? throw new InvalidOperationException($"Unresolvable $ref '{reference}'.");
        }

        return current;
    }

    private static bool TemplateMatches(string template, string requestPath)
    {
        var pattern = "^" + Regex.Replace(Regex.Escape(template), @"\\\{[^}]+}", "[^/]+") + "$";
        return Regex.IsMatch(requestPath, pattern, RegexOptions.CultureInvariant);
    }

    private static JsonNode RewriteRefs(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (key == "$ref" && value?.GetValue<string>() is { } reference && reference.StartsWith(ComponentSchemaRef, StringComparison.Ordinal))
                    {
                        obj[key] = "#/$defs/" + reference[ComponentSchemaRef.Length..];
                    }
                    else if (value is not null)
                    {
                        RewriteRefs(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        RewriteRefs(item);
                    }
                }

                break;
        }

        return node;
    }

    // YAML scalars become JSON values by YAML 1.2 core-schema rules: plain scalars may be null, bool or
    // numbers; quoted scalars are always strings (so '200' response keys stay strings).
    private static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(kv =>
            KeyValuePair.Create(((YamlScalarNode)kv.Key).Value!, ToJson(kv.Value)))),
        YamlSequenceNode seq => new JsonArray([.. seq.Children.Select(ToJson)]),
        YamlScalarNode scalar => ScalarToJson(scalar),
        _ => throw new NotSupportedException($"Unsupported YAML node {node.NodeType}."),
    };

    private static JsonValue? ScalarToJson(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        return value switch
        {
            "" or "~" or "null" => null,
            "true" => JsonValue.Create(true),
            "false" => JsonValue.Create(false),
            _ when long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) => JsonValue.Create(l),
            _ when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
            _ => JsonValue.Create(value),
        };
    }
}

public sealed record ContractOperation(string Path, string Method, JsonObject Node)
{
    public string? Permission => Node["x-permission"]?.GetValue<string>();

    /// <summary>Lists the <c>signedUrl</c> security scheme, so a signed URL stands in for a bearer token.</summary>
    public bool AcceptsSignedUrl => Node["security"] is JsonArray security
        && security.Any(s => s is JsonObject requirement && requirement.ContainsKey("signedUrl"));

    /// <summary>Mapped only in the dev configuration (SPEC §13), so it's absent from every other server.</summary>
    public bool DevOnly => Node["x-dev-only"]?.GetValue<bool>() ?? false;

    public override string ToString() => $"{Method} {Path}";
}
