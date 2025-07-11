using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder.Distributed;

namespace BitcoinFinder
{
    public class DistributedCoordinatorServer
    {
        private readonly int _port;
        private TcpListener? _listener;

        public ConcurrentQueue<BlockTask> PendingTasks { get; } = new ConcurrentQueue<BlockTask>();
        public ConcurrentDictionary<string, AgentInfo> ConnectedAgents { get; } = new ConcurrentDictionary<string, AgentInfo>();

        public DistributedCoordinatorServer(int port = 5000)
        {
            _port = port;
        }

        public async Task StartServerAsync(CancellationToken token = default)
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Console.WriteLine($"[SERVER] Started on port {_port}");

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = HandleClientAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
            }
            catch (ObjectDisposedException)
            {
                // listener stopped
            }
        }

        public void StopServer()
        {
            _listener?.Stop();
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                Console.WriteLine($"[SERVER] Client connected {endpoint}");
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                while (client.Connected)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync();
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    BitcoinFinder.Distributed.Message msg;
                    try
                    {
                        msg = BitcoinFinder.Distributed.Message.FromJson(line);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SERVER] Invalid message: {ex.Message}\nRaw: {line}");
                        continue;
                    }

                    var response = await ProcessMessageAsync(msg, endpoint);
                    if (response != null)
                    {
                        await writer.WriteLineAsync(response.ToJson());
                    }
                    else
                    {
                        // Логируем нераспознанное сообщение
                        Console.WriteLine($"[SERVER] Unhandled message type: {msg.Type} | Full: {System.Text.Json.JsonSerializer.Serialize(msg)}");
                    }
                }
            }
        }

        private async Task<BitcoinFinder.Distributed.Message?> ProcessMessageAsync(BitcoinFinder.Distributed.Message msg, string ip)
        {
            switch (msg.Type)
            {
                case MessageType.AGENT_REGISTER:
                    var agent = new AgentInfo
                    {
                        AgentId = msg.AgentId,
                        IpAddress = ip,
                        Status = "Idle",
                        LastHeartbeat = DateTime.UtcNow
                    };
                    ConnectedAgents[msg.AgentId] = agent;
                    Console.WriteLine($"[SERVER] Registered agent {msg.AgentId}");
                    return new BitcoinFinder.Distributed.Message
                    {
                        Type = MessageType.AGENT_REGISTER,
                        AgentId = msg.AgentId,
                        Data = JsonSerializer.SerializeToElement(new { status = "OK" }),
                        Timestamp = DateTime.UtcNow
                    };

                case MessageType.AGENT_REQUEST_TASK:
                    if (PendingTasks.TryDequeue(out var task))
                    {
                        task.AssignedToAgent = msg.AgentId;
                        agent = ConnectedAgents[msg.AgentId];
                        agent.CurrentBlockId = task.BlockId;
                        ConnectedAgents[msg.AgentId] = agent;
                        return new BitcoinFinder.Distributed.Message
                        {
                            Type = MessageType.SERVER_TASK_ASSIGNED,
                            AgentId = msg.AgentId,
                            Data = JsonSerializer.SerializeToElement(task),
                            Timestamp = DateTime.UtcNow
                        };
                    }
                    else
                    {
                        return new BitcoinFinder.Distributed.Message
                        {
                            Type = MessageType.SERVER_NO_TASKS,
                            AgentId = msg.AgentId,
                            Timestamp = DateTime.UtcNow
                        };
                    }
            }
            await Task.Yield();
            return null;
        }

        public void CreateTask(int blockId, long start, long end)
        {
            var task = new BlockTask
            {
                BlockId = blockId,
                StartIndex = start,
                EndIndex = end,
                Status = "Pending"
            };
            PendingTasks.Enqueue(task);
        }
    }
}
