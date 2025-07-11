using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitcoinFinder;

namespace BitcoinFinder.Distributed
{
    public enum MessageType
    {
        AGENT_REGISTER,
        AGENT_REQUEST_TASK,
        SERVER_TASK_ASSIGNED,
        SERVER_NO_TASKS,
        AGENT_REPORT_PROGRESS,
        AGENT_BLOCK_COMPLETED,
        AGENT_REPORT_RESULT
    }

    public class Message
    {
        public MessageType Type { get; set; }
        public string AgentId { get; set; } = string.Empty;
        public JsonElement? Data { get; set; }
        public DateTime Timestamp { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static Message FromJson(string json)
        {
            return JsonSerializer.Deserialize<Message>(json)!;
        }
    }

    public class BlockTask
    {
        public int BlockId { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
        public SearchParameters SearchParams { get; set; } = new SearchParameters();
        public string Status { get; set; } = string.Empty;
        public string? AssignedToAgent { get; set; }
    }

    public class AgentInfo
    {
        public string AgentId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int CurrentBlockId { get; set; }
        public double Speed { get; set; }
        public DateTime LastHeartbeat { get; set; }
    }
}
