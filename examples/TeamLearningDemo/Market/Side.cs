// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamLearningDemo.Market;

/// <summary>Which side of the price a zone is on.</summary>
[JsonConverter(typeof(SideJsonConverter))]
public enum Side
{
    Support,
    Resistance,
}

/// <summary>
/// The only place a side becomes text or text becomes a side. Strings stay at the boundaries (JSON
/// documents, <c>params.json</c> keys, activity inputs, the CLI and the skill output); inside the
/// market code a side is always a <see cref="Side"/>.
/// </summary>
public static class SideNames
{
    /// <summary>The side's name as it appears in JSON and in <c>params.json</c>.</summary>
    public static string Name(this Side side) => side switch
    {
        Side.Support => "support",
        Side.Resistance => "resistance",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
    };

    /// <summary>The side named <paramref name="name"/>; any other name than the two sides is an error.</summary>
    public static Side Parse(string? name) => name switch
    {
        "support" => Side.Support,
        "resistance" => Side.Resistance,
        _ => throw new ArgumentException($"side must be \"support\" or \"resistance\", not \"{name}\""),
    };
}

/// <summary>Writes a side by its name, so a serialized <see cref="Zone"/> keeps <c>"side": "support"</c>.</summary>
internal sealed class SideJsonConverter : JsonConverter<Side>
{
    public override Side Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return SideNames.Parse(reader.GetString());
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Side value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name());
}
