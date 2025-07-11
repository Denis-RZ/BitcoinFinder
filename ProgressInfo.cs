namespace BitcoinFinder
{
    public class ProgressInfo
    {
        public System.Numerics.BigInteger Current { get; set; }
        public System.Numerics.BigInteger Total { get; set; }
        public double Percentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public double Rate { get; set; }
        public System.TimeSpan Remaining { get; set; }
        public List<string> CurrentPhrases { get; set; } = new List<string>();
        public string? CurrentPrivateKey { get; set; }
    }
} 