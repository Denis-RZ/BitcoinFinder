using System;
using System.Text.Json;
using BitcoinFinder.Distributed;
using BitcoinFinder;
using ProtoMessage = BitcoinFinder.Distributed.Message;
using Xunit;

namespace DistributedProtocolTests;

public class UnitTest1
{
    [Fact]
    public void MessageSerializationPreservesFields()
    {
        var task = new BlockTask
        {
            BlockId = 5,
            StartIndex = 10,
            EndIndex = 20,
            SearchParams = new SearchParameters
            {
                SeedPhrase = "test",
                BitcoinAddress = "addr",
                WordCount = 12,
                FullSearch = false,
                ThreadCount = 1
            },
            Status = "InProgress",
            AssignedToAgent = "agent-1"
        };
        var message = new ProtoMessage
        {
            Type = MessageType.AGENT_REPORT_PROGRESS,
            AgentId = "agent-1",
            Data = JsonSerializer.SerializeToElement(task),
            Timestamp = DateTime.UtcNow
        };

        string json = message.ToJson();
        var restored = ProtoMessage.FromJson(json);

        Assert.Equal(message.Type, restored.Type);
        Assert.Equal(message.AgentId, restored.AgentId);
        Assert.Equal(message.Timestamp.ToString("O"), restored.Timestamp.ToString("O"));

        var restoredTask = restored.Data?.Deserialize<BlockTask>();
        Assert.NotNull(restoredTask);
        Assert.Equal(5, restoredTask!.BlockId);
        Assert.Equal(10, restoredTask.StartIndex);
        Assert.Equal(20, restoredTask.EndIndex);
        Assert.Equal("InProgress", restoredTask.Status);
        Assert.Equal("agent-1", restoredTask.AssignedToAgent);
        Assert.Equal("test", restoredTask.SearchParams.SeedPhrase);
        Assert.Equal("addr", restoredTask.SearchParams.BitcoinAddress);
        Console.WriteLine("Serialization test passed");
    }
}
