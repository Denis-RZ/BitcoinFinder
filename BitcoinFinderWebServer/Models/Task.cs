namespace BitcoinFinderWebServer.Models
{
    public class SearchTask
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? AssignedTo { get; set; }
        public SearchParameters Parameters { get; set; } = new();
        public long TotalCombinations { get; set; } = 0;
        public long ProcessedCombinations { get; set; } = 0;
        public double Progress => TotalCombinations > 0 ? (double)ProcessedCombinations / TotalCombinations : 0;
        public string? Result { get; set; }
        public string? FoundSeedPhrase { get; set; }
        public string? FoundAddress { get; set; }
        
        // Добавляем поддержку блоков для совместимости с WinForms
        public List<SearchBlock> Blocks { get; set; } = new();
        public bool EnableServerSearch { get; set; } = true;
        public int ServerThreads { get; set; } = 2;
    }

    public class SearchParameters
    {
        public string TargetAddress { get; set; } = "";
        public string KnownWords { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public string Language { get; set; } = "english";
        public long StartIndex { get; set; } = 0;
        public long EndIndex { get; set; } = 0;
        public int BatchSize { get; set; } = 1000;
        public long BlockSize { get; set; } = 100000; // Размер блока для совместимости
    }

    // Модели для совместимости с WinForms DistributedServer
    public class SearchBlock
    {
        public int BlockId { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
        public int WordCount { get; set; }
        public string TargetAddress { get; set; } = "";
        public BlockStatus Status { get; set; }
        public string? AssignedTo { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long CurrentIndex { get; set; }
        public DateTime? LastProgressAt { get; set; }
        public int Priority { get; set; }
    }

    public enum BlockStatus
    {
        Pending,
        Assigned,
        Completed,
        Failed
    }

    public class AgentStats
    {
        public string AgentId { get; set; } = "";
        public long ProcessedCount { get; set; }
        public double CurrentRate { get; set; }
        public int CompletedBlocks { get; set; }
        public long TotalProcessed { get; set; }
        public DateTime LastUpdate { get; set; }
        public double EtaSeconds { get; set; } = -1;
    }

    public class ServerStats
    {
        public int ConnectedAgents { get; set; }
        public int PendingBlocks { get; set; }
        public int AssignedBlocks { get; set; }
        public int CompletedBlocks { get; set; }
        public long TotalProcessed { get; set; }
        public long TotalCombinations { get; set; }
        public int FoundResults { get; set; }
        public TimeSpan Uptime { get; set; }
        public List<AgentStats> AgentStats { get; set; } = new();
    }

    // Модели для TCP-протокола совместимости
    public class AgentConnection
    {
        public string AgentId { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public DateTime ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsConnected { get; set; }
    }

    // Новые модели для синхронизации состояния
    public class AgentState
    {
        public string AgentId { get; set; } = "";
        public string AgentName { get; set; } = "";
        public int Threads { get; set; } = 1;
        public int? LastBlockId { get; set; }
        public long? LastIndex { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsConnected { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public long TotalProcessed { get; set; }
        public int CompletedBlocks { get; set; }
        public double CurrentRate { get; set; }
    }

    public class ServerState
    {
        public DateTime LastSaveTime { get; set; }
        public long TotalProcessed { get; set; }
        public int CompletedBlocksCount { get; set; }
        public int TotalBlocks { get; set; }
        public long LastProcessedIndex { get; set; }
        public List<string> FoundResults { get; set; } = new();
        public Dictionary<string, AgentState> AgentStates { get; set; } = new();
        public List<SearchBlock> PendingBlocks { get; set; } = new();
        public List<SearchBlock> AssignedBlocks { get; set; } = new();
        public List<SearchBlock> CompletedBlocks { get; set; } = new();
    }

    // Модели для API запросов/ответов
    public class AgentRegistrationRequest
    {
        public string Name { get; set; } = "";
        public int Threads { get; set; } = 1;
        public string Status { get; set; } = "Connected";
    }

    public class TaskResult
    {
        public string TaskId { get; set; } = "";
        public string Status { get; set; } = "";
        public string Result { get; set; } = "";
        public long ProcessedCombinations { get; set; } = 0;
        public TimeSpan ProcessingTime { get; set; }
        public string? FoundSeedPhrase { get; set; }
        public string? FoundAddress { get; set; }
    }

    public class BlockResult
    {
        public int BlockId { get; set; }
        public string Status { get; set; } = "";
        public long ProcessedCombinations { get; set; } = 0;
        public string? FoundSeedPhrase { get; set; }
        public string? FoundAddress { get; set; }
        public long FoundIndex { get; set; } = -1;
    }
} 