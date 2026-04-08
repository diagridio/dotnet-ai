// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Creates <see cref="AgentResponse"/> instances for integration tests using reflection, avoiding
/// a hard dependency on internal constructors that may change across package versions.
/// </summary>
public static class AgentRunResponseFactory
{
    public static AgentResponse CreateWithText(string text)
    {
        var responseType = typeof(AgentResponse);
        var message = CreateChatMessage(text);

        var response =
            TryCreateWithMessages(responseType, message)
            ?? TryCreateDefault(responseType)
            ?? RuntimeHelpers.GetUninitializedObject(responseType);

        if (!TrySetText(response, text))
        {
            TrySetMessages(response, message);
        }

        return (AgentResponse)response;
    }

    private static object? TryCreateWithMessages(Type responseType, ChatMessage message)
    {
        foreach (var ctor in responseType.GetConstructors(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length != 1) continue;

            var paramType = parameters[0].ParameterType;
            if (paramType == typeof(ChatMessage))
                return ctor.Invoke([message]);

            if (IsMessageListType(paramType))
            {
                var list = CreateMessageList(paramType, message);
                if (list is not null) return ctor.Invoke([list]);
            }
        }

        return null;
    }

    private static object? TryCreateDefault(Type responseType)
    {
        var ctor = responseType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, Type.EmptyTypes, modifiers: null);
        return ctor?.Invoke(null);
    }

    private static bool TrySetText(object response, string text)
    {
        var prop = response.GetType().GetProperty("Text",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop is { CanWrite: true })
        {
            prop.SetValue(response, text);
            return true;
        }

        var field = response.GetType().GetField("_text",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? response.GetType().GetField("Text",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field is { FieldType: { } ft } && ft == typeof(string))
        {
            field.SetValue(response, text);
            return true;
        }

        return false;
    }

    private static bool TrySetMessages(object response, ChatMessage message)
    {
        var type = response.GetType();
        foreach (var prop in type.GetProperties(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!prop.CanWrite || !IsMessageListType(prop.PropertyType)) continue;
            var list = CreateMessageList(prop.PropertyType, message);
            if (list is null) continue;
            prop.SetValue(response, list);
            return true;
        }

        foreach (var field in type.GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!IsMessageListType(field.FieldType)) continue;
            var list = CreateMessageList(field.FieldType, message);
            if (list is null) continue;
            field.SetValue(response, list);
            return true;
        }

        return false;
    }

    private static bool IsMessageListType(Type candidate)
    {
        if (candidate == typeof(ChatMessage) || candidate == typeof(List<ChatMessage>)) return true;
        if (candidate.IsArray) return candidate.GetElementType() == typeof(ChatMessage);
        if (!candidate.IsGenericType) return false;

        var def = candidate.GetGenericTypeDefinition();
        return (def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) ||
                def == typeof(IList<>) || def == typeof(IReadOnlyCollection<>) ||
                def == typeof(ICollection<>))
               && candidate.GetGenericArguments()[0] == typeof(ChatMessage);
    }

    private static object? CreateMessageList(Type listType, ChatMessage message)
    {
        if (listType == typeof(ChatMessage)) return message;
        if (listType == typeof(ChatMessage[])) return new[] { message };

        if (listType == typeof(List<ChatMessage>) ||
            listType == typeof(IReadOnlyList<ChatMessage>) ||
            listType == typeof(IList<ChatMessage>) ||
            listType == typeof(IEnumerable<ChatMessage>) ||
            listType == typeof(IReadOnlyCollection<ChatMessage>) ||
            listType == typeof(ICollection<ChatMessage>))
        {
            return new List<ChatMessage> { message };
        }

        if (listType.IsArray && listType.GetElementType() == typeof(ChatMessage))
            return new[] { message };

        return null;
    }

    private static ChatMessage CreateChatMessage(string text)
    {
        var type = typeof(ChatMessage);
        var ctor = type.GetConstructor([typeof(ChatRole), typeof(string)]);
        if (ctor is not null)
            return (ChatMessage)ctor.Invoke([ChatRole.Assistant, text]);

        var fallback = type.GetConstructor([typeof(string)]);
        if (fallback is not null)
            return (ChatMessage)fallback.Invoke([text]);

        return (ChatMessage)RuntimeHelpers.GetUninitializedObject(type);
    }
}
