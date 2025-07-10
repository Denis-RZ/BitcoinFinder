using System;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder;
using BitcoinFinder.Distributed;
using Xunit;

namespace DistributedProtocolTests;

public class AgentClientTests
{
    [Fact]
    public async Task ClientWorkflowSuccess()
    {
        var server = new DistributedCoordinatorServer(5001);
        using var cts = new CancellationTokenSource();
        var serverTask = server.StartServerAsync(cts.Token);
        await Task.Delay(200);
        server.CreateTask(1, 0, 5);

        var client = new DistributedAgentClient();
        Assert.True(await client.ConnectToServer("127.0.0.1", 5001));
        await client.SendRegisterMessage("agent1");
        var registerResp = await client.ReceiveMessage();
        Assert.NotNull(registerResp);
        Assert.Equal(MessageType.AGENT_REGISTER, registerResp!.Type);

        await client.RequestTask("agent1");
        var task = await client.ReceiveTask();
        Assert.NotNull(task);
        await client.ProcessTestBlock(task!);

        cts.Cancel();
        server.StopServer();
        await serverTask;
        Console.WriteLine("CLIENT TEST PASSED");
    }

    [Fact]
    public async Task ClientConnectionFailure()
    {
        var client = new DistributedAgentClient();
        bool connected = await client.ConnectToServer("127.0.0.1", 5999);
        Assert.False(connected);
    }
}
