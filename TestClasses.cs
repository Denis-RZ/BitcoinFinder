using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace BitcoinFinder
{
    public class SearchParameters
    {
        public string SeedPhrase { get; set; } = "";
        public string BitcoinAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public bool FullSearch { get; set; } = false;
        public int ThreadCount { get; set; } = 1;
        public string? ProgressFile { get; set; } = null;
    }

    public class ProgressData
    {
        public string SeedPhrase { get; set; } = "";
        public string BitcoinAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public bool FullSearch { get; set; } = false;
        public int ThreadCount { get; set; } = 1;
        public DateTime Timestamp { get; set; }
        public string CurrentCombination { get; set; } = "0";
        public string TotalCombinations { get; set; } = "0";
        public string LastCheckedPhrase { get; set; } = "";
    }

    public class ProgressInfo
    {
        public BigInteger Current { get; set; }
        public BigInteger Total { get; set; }
        public double Percentage { get; set; }
        public string Status { get; set; } = "";
        public double Rate { get; set; }
        public TimeSpan Remaining { get; set; }
        public List<string> CurrentPhrases { get; set; } = new List<string>();
        public string? CurrentPrivateKey { get; set; }
    }

    public class DistributedMasterServer
    {
        public string BitcoinAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public bool FullSearch { get; set; } = false;
        public int BlockSize { get; set; } = 10000;
        public long TotalCombinations { get; set; } = 0;

        private List<BlockInfo> allBlocks = new List<BlockInfo>();
        private Queue<BlockInfo> freeBlocks = new Queue<BlockInfo>();
        private List<BlockInfo> assignedBlocks = new List<BlockInfo>();
        private List<BlockInfo> doneBlocks = new List<BlockInfo>();
        public List<BlockInfo> BlockQueue => allBlocks;
        public List<AgentInfo> AgentList { get; set; } = new List<AgentInfo>();
        public List<string> FoundResults { get; set; } = new List<string>();
        private int nextBlockId = 1;
        private object syncRoot = new object();
        private string stateFile = "server_state.json";

        public DistributedMasterServer() { }

        public void InitBlocks(long totalCombinations, int blockSize)
        {
            lock (syncRoot)
            {
                allBlocks.Clear();
                freeBlocks.Clear();
                assignedBlocks.Clear();
                doneBlocks.Clear();
                nextBlockId = 1;
                long start = 0;
                while (start < totalCombinations)
                {
                    long end = Math.Min(start + blockSize - 1, totalCombinations - 1);
                    var block = new BlockInfo
                    {
                        BlockId = nextBlockId++,
                        StartIndex = start,
                        EndIndex = end,
                        Status = "free",
                        AssignedAgent = "",
                        Progress = "0%",
                        LastUpdate = DateTime.Now
                    };
                    allBlocks.Add(block);
                    freeBlocks.Enqueue(block);
                    start = end + 1;
                }
                SaveState();
            }
        }

        public BlockInfo? GetNextBlock(string agentId)
        {
            lock (syncRoot)
            {
                if (freeBlocks.Count == 0) return null;
                var block = freeBlocks.Dequeue();
                block.Status = "assigned";
                block.AssignedAgent = agentId;
                block.LastUpdate = DateTime.Now;
                assignedBlocks.Add(block);
                SaveState();
                return block;
            }
        }

        public void ReportProgress(int blockId, string agentId, long currentIndex)
        {
            lock (syncRoot)
            {
                var block = assignedBlocks.FirstOrDefault(b => b.BlockId == blockId && b.AssignedAgent == agentId);
                if (block != null)
                {
                    double percent = 100.0 * (currentIndex - block.StartIndex) / (block.EndIndex - block.StartIndex + 1);
                    block.Progress = $"{percent:F2}%";
                    block.LastUpdate = DateTime.Now;
                    SaveState();
                }
            }
        }

        public void ReleaseBlock(int blockId, string agentId)
        {
            lock (syncRoot)
            {
                var block = assignedBlocks.FirstOrDefault(b => b.BlockId == blockId && b.AssignedAgent == agentId);
                if (block != null)
                {
                    block.Status = "done";
                    block.LastUpdate = DateTime.Now;
                    assignedBlocks.Remove(block);
                    doneBlocks.Add(block);
                    SaveState();
                }
            }
        }

        public void ReportFound(string phrase)
        {
            lock (syncRoot)
            {
                if (!FoundResults.Contains(phrase))
                    FoundResults.Add(phrase);
                SaveState();
            }
        }

        public void ReassignStaleBlocks(TimeSpan timeout)
        {
            lock (syncRoot)
            {
                var now = DateTime.Now;
                var stale = assignedBlocks.Where(b => (now - b.LastUpdate) > timeout).ToList();
                foreach (var block in stale)
                {
                    block.Status = "free";
                    block.AssignedAgent = "";
                    block.Progress = "0%";
                    block.LastUpdate = DateTime.Now;
                    assignedBlocks.Remove(block);
                    freeBlocks.Enqueue(block);
                }
                SaveState();
            }
        }

        public void SaveState()
        {
            try
            {
                var state = new ServerState
                {
                    AllBlocks = allBlocks,
                    FoundResults = FoundResults,
                    BitcoinAddress = BitcoinAddress,
                    WordCount = WordCount,
                    FullSearch = FullSearch,
                    BlockSize = BlockSize,
                    TotalCombinations = TotalCombinations
                };
                File.WriteAllText(stateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public void LoadState()
        {
            try
            {
                if (!File.Exists(stateFile)) return;
                var state = JsonSerializer.Deserialize<ServerState>(File.ReadAllText(stateFile));
                if (state != null)
                {
                    allBlocks = state.AllBlocks ?? new List<BlockInfo>();
                    freeBlocks = new Queue<BlockInfo>(allBlocks.Where(b => b.Status == "free"));
                    assignedBlocks = allBlocks.Where(b => b.Status == "assigned").ToList();
                    doneBlocks = allBlocks.Where(b => b.Status == "done").ToList();
                    FoundResults = state.FoundResults ?? new List<string>();
                    BitcoinAddress = state.BitcoinAddress;
                    WordCount = state.WordCount;
                    FullSearch = state.FullSearch;
                    BlockSize = state.BlockSize;
                    TotalCombinations = state.TotalCombinations;
                }
            }
            catch { }
        }

        public class ServerState
        {
            public List<BlockInfo>? AllBlocks { get; set; }
            public List<string>? FoundResults { get; set; }
            public string BitcoinAddress { get; set; } = "";
            public int WordCount { get; set; }
            public bool FullSearch { get; set; }
            public int BlockSize { get; set; }
            public long TotalCombinations { get; set; }
        }
    }

    public class BlockInfo
    {
        public int BlockId { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
        public string Status { get; set; } = "";
        public string AssignedAgent { get; set; } = "";
        public string Progress { get; set; } = "";
        public DateTime LastUpdate { get; set; }
    }

    public class AgentInfo
    {
        public string AgentId { get; set; } = "";
        public string IP { get; set; } = "";
        public string AssignedBlock { get; set; } = "";
        public DateTime LastReport { get; set; }
    }
} 