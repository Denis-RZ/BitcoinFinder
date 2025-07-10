using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder;
using BitcoinFinder.Distributed;
using Xunit;

namespace DistributedProtocolTests;

public class CoordinatorServerTests
{
    [Fact]
    public async Task ServerStartsAndRegistersAgent()
    {
        var server = new DistributedCoordinatorServer(5000);
        using var cts = new CancellationTokenSource();
        var serverTask = server.StartServerAsync(cts.Token);

        await Task.Delay(200); // allow server to start
        server.CreateTask(1, 0, 100);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 5000);

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        var register = new Message
        {
            Type = MessageType.AGENT_REGISTER,
            AgentId = "test-agent",
            Timestamp = DateTime.UtcNow
        };
        await writer.WriteLineAsync(register.ToJson());
        var respLine = await reader.ReadLineAsync();
        Assert.NotNull(respLine);
        var resp = Message.FromJson(respLine!);
        Assert.Equal(MessageType.AGENT_REGISTER, resp.Type);

        // clean up
        cts.Cancel();
        server.StopServer();
        await serverTask;

        Assert.True(server.ConnectedAgents.ContainsKey("test-agent"));
        Console.WriteLine("SERVER TEST PASSED");
    }
}
