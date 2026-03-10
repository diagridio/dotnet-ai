using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

public static class AgentRunResponseFactory
{
    public static AgentRunResponse CreateWithText(string text)
    {
        var responseType = typeof(AgentRunResponse);
        var message = CreateChatMessage(text);

        var response = TryCreateWithMessages(responseType, message) ?? TryCreateDefault(responseType) ??
                       RuntimeHelpers.GetUninitializedObject(responseType);

        if (TrySetText(response, text))
        {
            return (AgentRunResponse)response;
        }

        if (TrySetMessages(response, message))
        {
            return (AgentRunResponse)response;
        }

        return (AgentRunResponse)response;
    }

    private static object? TryCreateWithMessages(Type responseType, ChatMessage message)
    {
        var ctors = responseType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var ctor in ctors)
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 1)
            {
                var paramType = parameters[0].ParameterType;
                if (paramType == typeof(ChatMessage))
                {
                    return ctor.Invoke([message]);
                }

                if (IsMessageListType(paramType))
                {
                    var list = CreateMessageList(paramType, message);
                    if (list is not null)
                    {
                        return ctor.Invoke([list]);
                    }
                }
            }
        }

        return null;
    }

    private static object? TryCreateDefault(Type responseType)
    {
        var ctor = responseType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, Type.EmptyTypes, modifiers: null);
        return ctor?.Invoke(null);
    }

    private static bool TrySetText(object response, string text)
    {
        var prop = response.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop is { CanWrite: true })
        {
            prop.SetValue(response, text);
            return true;
        }

        var field = response.GetType().GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic) ??
                    response.GetType().GetField("Text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field is not null && field.FieldType == typeof(string))
        {
            field.SetValue(response, text);
            return true;
        }

        return false;
    }

    private static bool TrySetMessages(object response, ChatMessage message)
    {
        var responseType = response.GetType();
        var properties = responseType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var prop in properties)
        {
            if (!prop.CanWrite)
            {
                continue;
            }

            if (!IsMessageListType(prop.PropertyType))
            {
                continue;
            }

            var list = CreateMessageList(prop.PropertyType, message);
            if (list is null)
            {
                continue;
            }

            prop.SetValue(response, list);
            return true;
        }

        var fields = responseType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            if (!IsMessageListType(field.FieldType))
            {
                continue;
            }

            var list = CreateMessageList(field.FieldType, message);
            if (list is null)
            {
                continue;
            }

            field.SetValue(response, list);
            return true;
        }

        return false;
    }

    private static bool IsMessageListType(Type candidate)
    {
        if (candidate == typeof(ChatMessage) || candidate == typeof(List<ChatMessage>))
        {
            return true;
        }

        if (candidate.IsArray)
        {
            return candidate.GetElementType() == typeof(ChatMessage);
        }

        if (!candidate.IsGenericType)
        {
            return false;
        }

        var definition = candidate.GetGenericTypeDefinition();
        if (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IList<>) ||
            definition == typeof(IReadOnlyCollection<>) || definition == typeof(ICollection<>))
        {
            return candidate.GetGenericArguments()[0] == typeof(ChatMessage);
        }

        return false;
    }

    private static object? CreateMessageList(Type listType, ChatMessage message)
    {
        if (listType == typeof(ChatMessage))
        {
            return message;
        }

        if (listType == typeof(ChatMessage[]))
        {
            return new[] { message };
        }

        if (listType == typeof(List<ChatMessage>) || listType == typeof(IReadOnlyList<ChatMessage>) ||
            listType == typeof(IList<ChatMessage>) || listType == typeof(IEnumerable<ChatMessage>) ||
            listType == typeof(IReadOnlyCollection<ChatMessage>) || listType == typeof(ICollection<ChatMessage>))
        {
            return new List<ChatMessage> { message };
        }

        if (listType.IsGenericType)
        {
            var genericDefinition = listType.GetGenericTypeDefinition();
            if (genericDefinition == typeof(IEnumerable<>) || genericDefinition == typeof(IReadOnlyList<>) ||
                genericDefinition == typeof(IList<>) || genericDefinition == typeof(IReadOnlyCollection<>) ||
                genericDefinition == typeof(ICollection<>))
            {
                return new List<ChatMessage> { message };
            }
        }

        if (listType.IsArray && listType.GetElementType() == typeof(ChatMessage))
        {
            return new[] { message };
        }

        return null;
    }

    private static ChatMessage CreateChatMessage(string text)
    {
        var type = typeof(ChatMessage);
        var ctor = type.GetConstructor(new[] { typeof(ChatRole), typeof(string) });
        if (ctor is not null)
        {
            return (ChatMessage)ctor.Invoke(new object?[] { ChatRole.Assistant, text });
        }

        var fallback = type.GetConstructor(new[] { typeof(string) });
        if (fallback is not null)
        {
            return (ChatMessage)fallback.Invoke(new object?[] { text });
        }

        return (ChatMessage)RuntimeHelpers.GetUninitializedObject(type);
    }
}
