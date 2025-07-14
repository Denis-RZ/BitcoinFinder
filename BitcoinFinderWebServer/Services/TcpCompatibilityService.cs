using BitcoinFinderWebServer.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BitcoinFinderWebServer.Services
{
    public class TcpCompatibilityService : BackgroundService
    {
        private readonly TaskManager _taskManager;
        private readonly AgentManager _agentManager;
        private readonly ILogger<TcpCompatibilityService> _logger;
        private readonly int _port;
        
        private TcpListener? _tcpListener;
        private readonly ConcurrentDictionary<string, AgentConnection> _connectedAgents = new();
        private readonly ConcurrentDictionary<string, StreamWriter> _agentWriters = new();
        private CancellationTokenSource? _cancellationTokenSource;

        public TcpCompatibilityService(
            TaskManager taskManager, 
            AgentManager agentManager, 
            ILogger<TcpCompatibilityService> logger,
            IConfiguration configuration)
        {
            _taskManager = taskManager;
            _agentManager = agentManager;
            _logger = logger;
            _port = configuration.GetValue<int>("TcpPort", 5000);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            
            try
            {
                var port = _port;
                var maxPortAttempts = 10;
                
                for (int attempt = 0; attempt < maxPortAttempts; attempt++)
                {
                    try
                    {
                        _tcpListener = new TcpListener(IPAddress.Any, port);
                        _tcpListener.Start();
                        
                        _logger.LogInformation($"TCP-совместимый сервис запущен на порту {port}");
                        break;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        _tcpListener?.Stop();
                        port++;
                        _logger.LogWarning($"Порт {port - 1} занят, пробуем порт {port}");
                        
                        if (attempt == maxPortAttempts - 1)
                        {
                            _logger.LogError("Не удалось найти свободный порт для TCP-сервиса");
                            return;
                        }
                    }
                }
                
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync(_cancellationTokenSource.Token);
                        _ = HandleClientAsync(client, _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при принятии TCP-соединения");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в TCP-совместимом сервисе");
            }
            finally
            {
                _tcpListener?.Stop();
                _logger.LogInformation("TCP-совместимый сервис остановлен");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            _logger.LogInformation($"Новое TCP-соединение от {clientEndpoint}");

            string? agentId = null;

            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                while (!token.IsCancellationRequested && client.Connected)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        var message = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                        if (message == null) continue;

                        var command = GetStringValue(message, "command");
                        
                        switch (command)
                        {
                            case "HELLO":
                                agentId = await HandleHello(message, writer);
                                if (!string.IsNullOrEmpty(agentId))
                                {
                                    _agentWriters.TryAdd(agentId, writer);
                                }
                                break;

                            case "GOODBYE":
                                await HandleGoodbye(message, writer);
                                return;

                            case "HEARTBEAT":
                                await HandleHeartbeat(message, writer);
                                break;

                            case "GET_TASK":
                                await HandleGetTask(agentId, writer);
                                break;

                            case "TASK_ACCEPTED":
                                await HandleTaskAccepted(message, agentId, writer);
                                break;

                            case "TASK_COMPLETED":
                                await HandleTaskCompleted(message, agentId, writer);
                                break;

                            case "REPORT_PROGRESS":
                                await HandleProgressReport(message, agentId);
                                break;

                            case "REPORT_FOUND":
                                await HandleFoundReport(message, agentId);
                                break;

                            case "RELEASE_BLOCK":
                                await HandleBlockRelease(message, agentId, writer);
                                break;

                            default:
                                await SendErrorResponse(writer, "UNKNOWN_COMMAND", $"Неизвестная команда: {command}");
                                break;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Ошибка парсинга JSON от {ClientEndpoint}", clientEndpoint);
                        await SendErrorResponse(writer, "INVALID_JSON", "Неверный формат JSON");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка обработки сообщения от {ClientEndpoint}", clientEndpoint);
                        await SendErrorResponse(writer, "INTERNAL_ERROR", "Внутренняя ошибка сервера");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки TCP-клиента {ClientEndpoint}", clientEndpoint);
            }
            finally
            {
                if (!string.IsNullOrEmpty(agentId))
                {
                    _connectedAgents.TryRemove(agentId, out _);
                    _agentWriters.TryRemove(agentId, out _);
                    _logger.LogInformation($"Агент {agentId} отключен");
                }
                client.Close();
            }
        }

        private async Task<string?> HandleHello(Dictionary<string, object> message, StreamWriter writer)
        {
            var agentName = GetStringValue(message, "agentName");
            var threads = GetInt32Value(message, "threads");

            if (string.IsNullOrEmpty(agentName))
            {
                await SendErrorResponse(writer, "MISSING_AGENT_NAME", "Отсутствует имя агента");
                return null;
            }

            var agentId = Guid.NewGuid().ToString();
            var agent = new Agent
            {
                Id = agentId,
                Name = agentName,
                Threads = threads,
                Status = "Connected",
                LastSeen = DateTime.UtcNow,
                ConnectedAt = DateTime.UtcNow
            };

            await _agentManager.RegisterAgentAsync(agent);

            var connection = new AgentConnection
            {
                AgentId = agentId,
                Endpoint = "TCP",
                ConnectedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                IsConnected = true
            };

            _connectedAgents.TryAdd(agentId, connection);

            // Уведомляем TaskManager о переподключении агента
            await _taskManager.AgentReconnectedAsync(agentName);

            var response = new
            {
                command = "HELLO_ACK",
                agentId = agentId,
                message = "Агент успешно зарегистрирован"
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            _logger.LogInformation($"Агент {agentName} (ID: {agentId}) зарегистрирован через TCP");

            return agentId;
        }

        private async Task HandleGoodbye(Dictionary<string, object> message, StreamWriter writer)
        {
            var agentId = GetStringValue(message, "agentId");
            var agentName = GetStringValue(message, "agentName");
            
            if (!string.IsNullOrEmpty(agentId))
            {
                _connectedAgents.TryRemove(agentId, out _);
                _agentWriters.TryRemove(agentId, out _);
                await _agentManager.UnregisterAgentAsync(agentName);
                
                // Уведомляем TaskManager об отключении агента
                if (!string.IsNullOrEmpty(agentName))
                {
                    await _taskManager.AgentDisconnectedAsync(agentName);
                }
                
                _logger.LogInformation($"Агент {agentName} отключился");
            }

            var response = new { command = "GOODBYE_ACK" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        private async Task HandleHeartbeat(Dictionary<string, object> message, StreamWriter writer)
        {
            var agentId = GetStringValue(message, "agentId");
            
            if (!string.IsNullOrEmpty(agentId) && _connectedAgents.TryGetValue(agentId, out var connection))
            {
                connection.LastActivity = DateTime.UtcNow;
                await _agentManager.UpdateAgentActivityAsync(agentId);
            }

            var response = new { command = "HEARTBEAT_ACK" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        private async Task HandleGetTask(string? agentId, StreamWriter writer)
        {
            if (string.IsNullOrEmpty(agentId))
            {
                await SendErrorResponse(writer, "NO_AGENT_ID", "Неизвестный агент");
                return;
            }

            var block = await _taskManager.GetNextBlockAsync(agentId);
            
            if (block == null)
            {
                var response = new { command = "NO_BLOCKS" };
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                return;
            }

            var blockResponse = new
            {
                command = "ASSIGN_BLOCK",
                blockId = block.BlockId,
                startIndex = block.StartIndex,
                endIndex = block.EndIndex,
                wordCount = block.WordCount,
                targetAddress = block.TargetAddress
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(blockResponse));
        }

        private async Task HandleTaskAccepted(Dictionary<string, object> message, string? agentId, StreamWriter writer)
        {
            var blockId = GetInt32Value(message, "blockId");
            
            if (string.IsNullOrEmpty(agentId))
            {
                await SendErrorResponse(writer, "NO_AGENT_ID", "Неизвестный агент");
                return;
            }

            await _agentManager.UpdateAgentActivityAsync(agentId);
            
            var response = new { command = "TASK_ACCEPTED_ACK" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        private async Task HandleTaskCompleted(Dictionary<string, object> message, string? agentId, StreamWriter writer)
        {
            var blockId = GetInt32Value(message, "blockId");
            var processed = GetInt64Value(message, "processed");

            if (string.IsNullOrEmpty(agentId))
            {
                await SendErrorResponse(writer, "NO_AGENT_ID", "Неизвестный агент");
                return;
            }

            var result = new BlockResult
            {
                BlockId = blockId,
                Status = "Completed",
                ProcessedCombinations = processed
            };

            await _taskManager.ProcessBlockResultAsync(agentId, result);
            await _agentManager.UpdateAgentActivityAsync(agentId);

            var response = new { command = "TASK_COMPLETED_ACK" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        private async Task HandleProgressReport(Dictionary<string, object> message, string? agentId)
        {
            var blockId = GetInt32Value(message, "blockId");
            var currentIndex = GetInt64Value(message, "currentIndex");

            if (!string.IsNullOrEmpty(agentId))
            {
                await _taskManager.UpdateBlockProgressAsync(blockId, currentIndex);
                await _agentManager.UpdateAgentActivityAsync(agentId);
            }
        }

        private async Task HandleFoundReport(Dictionary<string, object> message, string? agentId)
        {
            var blockId = GetInt32Value(message, "blockId");
            var seedPhrase = GetStringValue(message, "seedPhrase");
            var address = GetStringValue(message, "address");
            var index = GetInt64Value(message, "index");

            if (!string.IsNullOrEmpty(agentId))
            {
                var result = new BlockResult
                {
                    BlockId = blockId,
                    Status = "Found",
                    ProcessedCombinations = index,
                    FoundSeedPhrase = seedPhrase,
                    FoundAddress = address,
                    FoundIndex = index
                };

                await _taskManager.ProcessBlockResultAsync(agentId, result);
                await _agentManager.UpdateAgentActivityAsync(agentId);

                _logger.LogWarning($"НАЙДЕНО РЕШЕНИЕ от агента {agentId}: {seedPhrase} -> {address}");
            }
        }

        private async Task HandleBlockRelease(Dictionary<string, object> message, string? agentId, StreamWriter writer)
        {
            var blockId = GetInt32Value(message, "blockId");
            var currentIndex = GetInt64Value(message, "currentIndex");

            if (!string.IsNullOrEmpty(agentId))
            {
                await _taskManager.ReleaseBlockAsync(blockId, agentId, currentIndex);
                await _agentManager.UpdateAgentActivityAsync(agentId);
            }

            var response = new { command = "BLOCK_RELEASED_ACK" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        private async Task SendErrorResponse(StreamWriter writer, string errorCode, string errorMessage)
        {
            var response = new
            {
                command = "ERROR",
                errorCode = errorCode,
                message = errorMessage
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        private string GetStringValue(Dictionary<string, object> message, string key)
        {
            return message.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";
        }

        private int GetInt32Value(Dictionary<string, object> message, string key)
        {
            if (message.TryGetValue(key, out var value))
            {
                if (value is int intValue) return intValue;
                if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return 0;
        }

        private long GetInt64Value(Dictionary<string, object> message, string key)
        {
            if (message.TryGetValue(key, out var value))
            {
                if (value is long longValue) return longValue;
                if (value is int intValue) return intValue;
                if (long.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return 0;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource?.Cancel();
            await base.StopAsync(cancellationToken);
        }
    }
} 