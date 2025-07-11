namespace BitcoinFinder
{
    public class SearchResult
    {
        public bool Found { get; set; }
        public string? SeedPhrase { get; set; }
        public string? PrivateKey { get; set; }
        public string? BitcoinAddress { get; set; }
        public long FoundAtIndex { get; set; }
        public long ProcessedCount { get; set; }
        public int CheckedCount { get; set; }
        public string? FoundPhrase { get; set; }
        public string? FoundPrivateKey { get; set; }
        public string? FoundAddress { get; set; }
        public long ProcessingTimeMs { get; set; }
        public string DeviceUsed { get; set; } = "CPU";
    }
} 