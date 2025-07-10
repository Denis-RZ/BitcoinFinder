namespace BitcoinFinder;

public class SearchParameters
{
    public string SeedPhrase { get; set; } = "";
    public string BitcoinAddress { get; set; } = "";
    public int WordCount { get; set; } = 12;
    public bool FullSearch { get; set; } = false;
    public int ThreadCount { get; set; } = 1;
    public string? ProgressFile { get; set; } = null;
}
