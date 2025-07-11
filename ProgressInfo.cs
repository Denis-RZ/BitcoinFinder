namespace BitcoinFinder
{
    public class ProgressInfo
    {
        public long Current { get; set; }
        public long Total { get; set; }
        public double Percentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public double Rate { get; set; }
        public long Remaining { get; set; }
        public string CurrentPhrases { get; set; } = string.Empty;
        public string CurrentPrivateKey { get; set; } = string.Empty;
    }
} 