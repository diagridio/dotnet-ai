using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentInvokerTests
{
    // -------------------------------------------------------------------------
    // GetAgent
    // -------------------------------------------------------------------------

    [Fact]
    public void GetAgent_ValidName_ReturnsAgentWithCorrectName()
    {
        var invoker = BuildInvoker();

        var agent = invoker.GetAgent("my-agent");

        Assert.NotNull(agent);
        Assert.Equal("my-agent", agent.Name);
    }

    [Fact]
    public void GetAgent_NullName_ThrowsArgumentNullException()
    {
        var invoker = BuildInvoker();

        Assert.Throws<ArgumentNullException>(() => invoker.GetAgent(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetAgent_WhitespaceOrEmptyName_ThrowsArgumentException(string name)
    {
        var invoker = BuildInvoker();

        Assert.Throws<ArgumentException>(() => invoker.GetAgent(name));
    }

    [Fact]
    public void GetAgent_ReturnsDifferentInstancesForSameName()
    {
        var invoker = BuildInvoker();

        var agent1 = invoker.GetAgent("agent");
        var agent2 = invoker.GetAgent("agent");

        Assert.NotSame(agent1, agent2);
        Assert.Equal(agent1.Name, agent2.Name);
    }

    // -------------------------------------------------------------------------
    // RunAgentAsync — null-argument guards
    // These guards fire before any workflow interaction, so no real DaprWorkflowClient is needed.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAgentAsync_NullAgent_ThrowsArgumentNullException()
    {
        var invoker = BuildInvoker();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            invoker.RunAgentAsync(null!));
    }

    // -------------------------------------------------------------------------
    // RunAgentAndDeserializeAsync (with ILogger) — null-argument guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithLogger_NullAgent_ThrowsArgumentNullException()
    {
        var invoker = BuildInvoker();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            invoker.RunAgentAndDeserializeAsync<TestPayload>(null!, NullLogger.Instance));
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithLogger_NullLogger_ThrowsArgumentNullException()
    {
        var invoker = BuildInvoker();
        var agent = new Mock<IDaprAIAgent>().Object;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            invoker.RunAgentAndDeserializeAsync<TestPayload>(agent, (ILogger)null!));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="DaprAgentInvoker"/> with null workflow client (acceptable for
    /// tests that only exercise argument-validation guards that fire before any workflow call).
    /// </summary>
    private static DaprAgentInvoker BuildInvoker() =>
        new(
            workflowClient: null!,
            loggerFactory: NullLoggerFactory.Instance,
            logger: NullLogger<DaprAgentInvoker>.Instance);
}
