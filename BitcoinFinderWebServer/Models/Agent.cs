namespace BitcoinFinderWebServer.Models
{
    public class Agent
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Threads { get; set; } = 1;
        public string Status { get; set; } = "Disconnected";
        public DateTime ConnectedAt { get; set; }
        public DateTime LastSeen { get; set; }
        public long TotalTasksProcessed { get; set; } = 0;
        public long TotalCombinationsProcessed { get; set; } = 0;
        public double ProcessingRate { get; set; } = 0;
    }
} 