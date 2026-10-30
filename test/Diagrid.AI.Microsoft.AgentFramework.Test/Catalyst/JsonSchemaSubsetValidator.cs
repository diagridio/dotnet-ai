// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Catalyst;

/// <summary>
/// Validates JSON against the subset of JSON Schema that dapr-agents' agent-metadata schema uses
/// ($ref into $defs, anyOf, type, properties, required, items, additionalProperties), so the
/// test needs no schema-validation package.
/// </summary>
/// <remarks>
/// It is stricter than JSON Schema on one point: a property the schema does not declare is an
/// error, unless additionalProperties allows it. That catches a renamed field (for example
/// "arguments" instead of "args") even where the schema itself would accept extra properties.
/// </remarks>
internal static class JsonSchemaSubsetValidator
{
    public static List<string> Validate(JsonElement instance, JsonElement schema)
    {
        var errors = new List<string>();
        Validate(instance, schema, schema, "$", errors);
        return errors;
    }

    private static void Validate(JsonElement instance, JsonElement schema, JsonElement root, string path, List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            Validate(instance, Resolve(root, reference.GetString()!), root, path, errors);
            return;
        }

        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            var branchErrors = new List<string>();
            foreach (var branch in anyOf.EnumerateArray())
            {
                var attempt = new List<string>();
                Validate(instance, branch, root, path, attempt);
                if (attempt.Count == 0)
                    return;
                branchErrors.AddRange(attempt);
            }

            errors.Add($"{path}: matches no anyOf branch ({string.Join("; ", branchErrors)})");
            return;
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(instance, type.GetString()!))
        {
            errors.Add($"{path}: expected {type.GetString()}, got {instance.ValueKind}");
            return;
        }

        if (instance.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in instance.EnumerateArray())
                Validate(item, items, root, $"{path}[{index++}]", errors);
        }

        if (instance.ValueKind == JsonValueKind.Object)
            ValidateObject(instance, schema, root, path, errors);
    }

    private static void ValidateObject(JsonElement instance, JsonElement schema, JsonElement root, string path, List<string> errors)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray())
            {
                if (!instance.TryGetProperty(name.GetString()!, out _))
                    errors.Add($"{path}: missing required property '{name.GetString()}'");
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties);
        var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);

        foreach (var property in instance.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                Validate(property.Value, propertySchema, root, propertyPath, errors);
            }
            else if (hasAdditional && additional.ValueKind == JsonValueKind.Object)
            {
                Validate(property.Value, additional, root, propertyPath, errors);
            }
            else if (!(hasAdditional && additional.ValueKind == JsonValueKind.True))
            {
                errors.Add($"{propertyPath}: property is not declared in the schema");
            }
        }
    }

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        const string prefix = "#/$defs/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported $ref '{reference}'.");

        return root.GetProperty("$defs").GetProperty(reference[prefix.Length..]);
    }

    private static bool MatchesType(JsonElement instance, string type) => type switch
    {
        "null" => instance.ValueKind == JsonValueKind.Null,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "array" => instance.ValueKind == JsonValueKind.Array,
        "object" => instance.ValueKind == JsonValueKind.Object,
        _ => throw new NotSupportedException($"Unsupported schema type '{type}'."),
    };
}
