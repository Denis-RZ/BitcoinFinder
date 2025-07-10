using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder;
using Xunit;

namespace DistributedProtocolTests;

public class WebMonitorTests
{
    [Fact]
    public async Task ServerProvidesStatusAndHtml()
    {
        var coord = new DistributedCoordinatorServer(5090);
        var web = new WebMonitorService(coord);
        using var cts = new CancellationTokenSource();
        var task = web.StartAsync(cts.Token);

        await Task.Delay(200);

        using var client = new HttpClient();
        string baseUrl = "http://localhost:8080";
        var html = await client.GetStringAsync(baseUrl + "/");
        Assert.Contains("Server Status: Running", html);

        var jsonStr = await client.GetStringAsync(baseUrl + "/api/status");
        using var doc = JsonDocument.Parse(jsonStr);
        Assert.True(doc.RootElement.TryGetProperty("UptimeSeconds", out _));

        cts.Cancel();
        await task;

        Console.WriteLine("WEB SERVER TEST PASSED");
    }
}
