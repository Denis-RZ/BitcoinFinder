#nullable enable
using System;
using System.Collections.Generic;

namespace BitcoinFinderAndroidNew.Services
{
    public class PrivateKeyParameters
    {
        public string TargetAddress { get; set; } = "";
        public KeyFormat Format { get; set; } = KeyFormat.Decimal;
        public NetworkType Network { get; set; } = NetworkType.Mainnet;
        public int ThreadCount { get; set; } = Environment.ProcessorCount;
        public long StartIndex { get; set; } = 0;
        public long EndIndex { get; set; } = 0;
    }

    public enum KeyFormat
    {
        Decimal,    // 1, 2, 3, ...
        Hex,        // 0x1, 0x2, 0x3, ...
    }

    public enum NetworkType
    {
        Mainnet,
        Testnet
    }

    public class PrivateKeyResult
    {
        public bool Found { get; set; } = false;
        public string PrivateKey { get; set; } = "";
        public string BitcoinAddress { get; set; } = "";
        public decimal Balance { get; set; } = 0;
        public DateTime FoundAt { get; set; } = DateTime.Now;
        public TimeSpan ProcessingTime { get; set; }
        public long ProcessedKeys { get; set; } = 0;
        public string Network { get; set; } = "Bitcoin";
        public long FoundAtIndex { get; set; } = 0;
    }

    public class ProgressInfo
    {
        public string CurrentKey { get; set; } = "";
        public string CurrentAddress { get; set; } = "";
        public long ProcessedKeys { get; set; } = 0;
        public long TotalKeys { get; set; } = 0;
        public double Progress { get; set; } = 0;
        public double Speed { get; set; } = 0; // keys per second (legacy)
        public long KeysPerSecond { get; set; } = 0; // keys per second (new)
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public string Status { get; set; } = "";
        public string TargetAddress { get; set; } = "";
    }

    // Модели для сохранения прогресса
    public class SearchProgress
    {
        public string TaskId { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string TargetAddress { get; set; } = "";
        public long CurrentIndex { get; set; } = 0;
        public long StartIndex { get; set; } = 0;
        public long EndIndex { get; set; } = 0;
        public KeyFormat Format { get; set; } = KeyFormat.Decimal;
        public NetworkType Network { get; set; } = NetworkType.Mainnet;
        public DateTime LastSaved { get; set; } = DateTime.Now;
        public DateTime StartTime { get; set; } = DateTime.Now;
        public TimeSpan ElapsedTime { get; set; }
        public bool IsActive { get; set; } = false;
        public PrivateKeyResult? FoundResult { get; set; }
    }

    public class FoundResult
    {
        public string PrivateKey { get; set; } = "";
        public string BitcoinAddress { get; set; } = ""; // Legacy
        public string Address { get; set; } = ""; // New
        public decimal Balance { get; set; } = 0;
        public DateTime FoundAt { get; set; } = DateTime.Now;
        public long FoundAtIndex { get; set; } = 0;
        public TimeSpan ProcessingTime { get; set; } = TimeSpan.Zero;
    }

    public class AppSettings
    {
        public int DefaultThreadCount { get; set; } = Environment.ProcessorCount;
        public int AutoSaveInterval { get; set; } = 1000; // Сохранять каждые N ключей
        public bool EnableNotifications { get; set; } = true;
        public bool RunInBackground { get; set; } = true;
        public string ProgressFilePath { get; set; } = "progress.json";
        public string ResultsFilePath { get; set; } = "results.json";
    }
} 