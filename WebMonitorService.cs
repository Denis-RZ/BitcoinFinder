using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BitcoinFinder
{
    public class WebMonitorService
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly DistributedCoordinatorServer _coordinator;
        private DateTime _startTime;

        public WebMonitorService(DistributedCoordinatorServer coordinator, int port = 8080)
        {
            _coordinator = coordinator;
            _listener.Prefixes.Add($"http://*:{port}/");
        }

        public async Task StartAsync(CancellationToken token = default)
        {
            _startTime = DateTime.UtcNow;
            _listener.Start();
            Console.WriteLine("[WEB] Listening on port 8080");
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context));
                }
            }
            catch (HttpListenerException)
            {
                // listener stopped
            }
            finally
            {
                _listener.Stop();
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            var path = context.Request.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

            switch (path)
            {
                case "/":
                    await RespondHtml(context, "<html><body><h1>Server Status: Running</h1></body></html>");
                    break;
                case "/api/agents":
                    await RespondJson(context, _coordinator.ConnectedAgents.Values);
                    break;
                case "/api/tasks":
                    await RespondJson(context, _coordinator.PendingTasks.ToArray());
                    break;
                case "/api/status":
                    var status = new
                    {
                        UptimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds,
                        Agents = _coordinator.ConnectedAgents.Count,
                        Tasks = _coordinator.PendingTasks.Count
                    };
                    await RespondJson(context, status);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    break;
            }
        }

        private static async Task RespondHtml(HttpListenerContext ctx, string html)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }

        private static async Task RespondJson(HttpListenerContext ctx, object data)
        {
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }
}
