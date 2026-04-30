using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;
using Moq;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprSessionExtensionsTests
{
    // =========================================================================
    // GetSessionInstanceId
    // =========================================================================

    [Fact]
    public void GetSessionInstanceId_NullSession_ReturnsNull()
    {
        AgentSession? session = null;

        var result = session.GetSessionInstanceId();

        Assert.Null(result);
    }

    [Fact]
    public void GetSessionInstanceId_SessionWithoutKey_ReturnsNull()
    {
        // TestAgentThread is an AgentSession without the Dapr session key in its StateBag
        AgentSession session = new TestAgentThread();

        var result = session.GetSessionInstanceId();

        Assert.Null(result);
    }

    [Fact]
    public void GetSessionInstanceId_SessionWithKey_ReturnsInstanceId()
    {
        var session = new TestAgentThread();
        session.StateBag.SetValue(DaprSessionConstants.SessionInstanceIdKey, "session-abc-123");

        var result = session.GetSessionInstanceId();

        Assert.Equal("session-abc-123", result);
    }

    [Fact]
    public void GetSessionInstanceId_SessionWithDifferentKey_ReturnsNull()
    {
        var session = new TestAgentThread();
        session.StateBag.SetValue("some.other.key", "should-not-be-returned");

        var result = session.GetSessionInstanceId();

        Assert.Null(result);
    }

    // =========================================================================
    // AttachSession
    // =========================================================================

    [Fact]
    public void AttachSession_ValidInstanceId_ReturnsNonNullSession()
    {
        var invoker = Mock.Of<IDaprAgentInvoker>();

        var session = invoker.AttachSession("session-id-42");

        Assert.NotNull(session);
    }

    [Fact]
    public void AttachSession_ValidInstanceId_SessionContainsProvidedId()
    {
        var invoker = Mock.Of<IDaprAgentInvoker>();
        const string expectedId = "session-id-42";

        var session = invoker.AttachSession(expectedId);

        Assert.Equal(expectedId, session.GetSessionInstanceId());
    }

    [Fact]
    public void AttachSession_NullInstanceId_ThrowsArgumentNullException()
    {
        var invoker = Mock.Of<IDaprAgentInvoker>();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() => invoker.AttachSession(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AttachSession_WhitespaceOrEmptyInstanceId_ThrowsArgumentException(string id)
    {
        var invoker = Mock.Of<IDaprAgentInvoker>();

        Assert.Throws<ArgumentException>(() => invoker.AttachSession(id));
    }

    [Fact]
    public void AttachSession_ReturnsDistinctSessionObjectEachCall()
    {
        var invoker = Mock.Of<IDaprAgentInvoker>();

        var session1 = invoker.AttachSession("id-1");
        var session2 = invoker.AttachSession("id-2");

        Assert.NotSame(session1, session2);
    }

    [Fact]
    public void AttachSession_StoredIdIsRetrievableViaGetSessionInstanceId()
    {
        var invoker = Mock.Of<IDaprAgentInvoker>();
        const string id = "roundtrip-id-99";

        var session = invoker.AttachSession(id);
        var retrieved = session.GetSessionInstanceId();

        Assert.Equal(id, retrieved);
    }
}
