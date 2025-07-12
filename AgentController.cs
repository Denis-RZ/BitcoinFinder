using System;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder.Distributed;
using System.Windows.Forms;
using ProtoMessage = BitcoinFinder.Distributed.Message;
using BitcoinFinder;

namespace BitcoinFinder
{

    public class AgentTaskInfo
    {
        public int BlockId { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
        public long EstimatedCombinations { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public long ProcessedCount { get; set; }
        public double CurrentRate { get; set; }
    }

    public class AgentController : IDisposable
    {
        private readonly Form parentForm;
        private readonly DistributedAgentClient agentClient;
        private readonly AdvancedSeedPhraseFinder finder;
        private CancellationTokenSource? cancellationSource;
        private Task? workerTask;
        private string currentAgentId = "";
        
        public bool IsConnected { get; private set; }
        public AgentState State { get; private set; } = AgentState.Disconnected;
        
        public event Action<string>? OnStatusChanged;
        public event Action<string>? OnLog;
        public event Action<AgentTaskInfo>? OnTaskReceived;
        public event Action<long, double>? OnProgressUpdate;

        public AgentController(Form form)
        {
            parentForm = form;
            agentClient = new DistributedAgentClient();
            finder = new AdvancedSeedPhraseFinder();
            
            // Подписываемся на события агентского клиента
            agentClient.OnLog += LogMessage;
            agentClient.OnProgress += UpdateProgress;
            agentClient.OnFound += HandleFoundResult;
            agentClient.OnStateChanged += HandleStateChanged;
        }

        public async Task<bool> ConnectAsync(string serverIp, int port, string agentName = "", int threads = 1)
        {
            try
            {
                SetState(AgentState.Connecting);
                OnStatusChanged?.Invoke($"Подключение к {serverIp}:{port}...");
                
                // Отменяем предыдущее подключение если есть
                await DisconnectAsync();
                
                cancellationSource = new CancellationTokenSource();
                
                // Загружаем сохраненный прогресс
                var savedProgress = agentClient.LoadProgress();
                if (savedProgress != null)
                {
                    LogMessage($"Найден сохраненный прогресс: блок {savedProgress.LastBlockId}, позиция {savedProgress.LastIndex:N0}");
                    LogMessage("Агент будет запрашивать продолжение с этой позиции");
                }
                
                // Подключаемся к серверу
                bool connected = await agentClient.ConnectToServer(serverIp, port, cancellationSource.Token);
                if (!connected)
                {
                    SetState(AgentState.Error);
                    OnStatusChanged?.Invoke("Не удалось подключиться к серверу");
                    return false;
                }

                SetState(AgentState.Connected);

                // Устанавливаем параметры агента
                agentClient.AgentName = string.IsNullOrEmpty(agentName) ? Environment.MachineName : agentName;
                agentClient.Threads = threads;
                
                // Регистрируемся как агент с параметрами
                currentAgentId = agentClient.AgentName + "_" + DateTime.Now.ToString("HHmmss");
                bool registered = await agentClient.RegisterAgent(currentAgentId, cancellationSource.Token);
                
                // Сохраняем конфигурацию агента
                agentClient.SaveAgentConfig();
                
                if (!registered)
                {
                    SetState(AgentState.Error);
                    OnStatusChanged?.Invoke("Сервер не подтвердил регистрацию");
                    return false;
                }

                SetState(AgentState.Registered);
                IsConnected = true;
                OnStatusChanged?.Invoke($"Подключено к {serverIp}:{port}");
                
                // Запускаем рабочий цикл
                workerTask = Task.Run(() => WorkerLoop(cancellationSource.Token));
                
                return true;
            }
            catch (Exception ex)
            {
                SetState(AgentState.Error);
                OnStatusChanged?.Invoke($"Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                IsConnected = false;
                SetState(AgentState.Disconnected);
                
                cancellationSource?.Cancel();
                
                if (workerTask != null)
                {
                    await workerTask;
                    workerTask = null;
                }
                
                await agentClient.DisconnectAsync();
                
                cancellationSource?.Dispose();
                cancellationSource = null;
                
                OnStatusChanged?.Invoke("Отключено");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"Ошибка отключения: {ex.Message}");
            }
        }

        private async Task WorkerLoop(CancellationToken token)
        {
            var heartbeatTimer = DateTime.Now;
            var taskRequestTimer = DateTime.Now;
            const int HeartbeatInterval = 30000; // 30 секунд
            const int TaskRequestInterval = 5000; // 5 секунд

            try
            {
                while (!token.IsCancellationRequested && IsConnected)
                {
                    var now = DateTime.Now;

                    // Отправляем heartbeat
                    if ((now - heartbeatTimer).TotalMilliseconds > HeartbeatInterval)
                    {
                        await SendHeartbeat();
                        heartbeatTimer = now;
                    }

                    // Запрашиваем задание
                    if ((now - taskRequestTimer).TotalMilliseconds > TaskRequestInterval)
                    {
                        await RequestAndProcessTask(token);
                        taskRequestTimer = now;
                    }

                    // Небольшая пауза
                    await Task.Delay(1000, token);
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("Рабочий цикл агента остановлен");
            }
            catch (Exception ex)
            {
                LogMessage($"Критическая ошибка в рабочем цикле: {ex.Message}");
                SetState(AgentState.Error);
                IsConnected = false;
                OnStatusChanged?.Invoke($"Потеряно соединение: {ex.Message}");
            }
        }

        private async Task SendHeartbeat()
        {
            try
            {
                var heartbeat = new
                {
                    command = "HEARTBEAT",
                    agentId = currentAgentId,
                    status = "working",
                    timestamp = DateTime.Now
                };
                
                await agentClient.SendMessage(heartbeat);
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка отправки heartbeat: {ex.Message}");
            }
        }

        private async Task RequestAndProcessTask(CancellationToken token)
        {
            try
            {
                if (State != AgentState.Registered && State != AgentState.Working) return;

                LogMessage($"Запрашиваю задание у сервера...");
                // Запрашиваем задание
                var task = await agentClient.RequestTask(currentAgentId, token);
                if (task != null)
                {
                    SetState(AgentState.Working);
                    LogMessage($"Получено задание: блок {task.BlockId} ({task.StartIndex}-{task.EndIndex})");
                    var taskInfo = new AgentTaskInfo
                    {
                        BlockId = task.BlockId,
                        StartIndex = task.StartIndex,
                        EndIndex = task.EndIndex,
                        EstimatedCombinations = task.EndIndex - task.StartIndex + 1
                    };

                    OnTaskReceived?.Invoke(taskInfo);

                    LogMessage($"Начинаю обработку блока {task.BlockId}");
                    // Обрабатываем задание
                    bool success = await agentClient.ProcessTaskWithProgress(task, "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ", token);
                    if (success)
                    {
                        SetState(AgentState.Registered); // Возвращаемся в состояние ожидания
                        LogMessage($"Блок {task.BlockId} обработан успешно");
                    }
                    else
                    {
                        LogMessage($"Ошибка при обработке блока {task.BlockId}");
                    }
                }
                else
                {
                    LogMessage($"Нет доступных заданий на сервере");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка запроса/обработки задания: {ex.Message}");
            }
        }

        private void SetState(AgentState newState)
        {
            if (State != newState)
            {
                State = newState;
                LogMessage($"Состояние агента изменено: {newState}");
            }
        }

        private void HandleStateChanged(AgentState newState)
        {
            SetState(newState);
        }

        private void LogMessage(string message)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void UpdateProgress(long current, double rate)
        {
            OnProgressUpdate?.Invoke(current, rate);
        }

        private void HandleFoundResult(string result)
        {
            OnLog?.Invoke($"*** НАЙДЕНО СОВПАДЕНИЕ *** {result}");
        }

        public void SaveAgentConfig()
        {
            agentClient.SaveAgentConfig();
        }

        public void Dispose()
        {
            DisconnectAsync().Wait(5000);
            agentClient?.Dispose();
            cancellationSource?.Dispose();
        }
    }
} 