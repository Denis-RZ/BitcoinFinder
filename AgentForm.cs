using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO; // Added for file operations
using System.Text.Json; // Added for JSON serialization/deserialization

namespace BitcoinFinder
{
    public partial class AgentForm : Form
    {
        private AgentController? agentController;

        // UI элементы для агента
        private TextBox txtServerIp;
        private TextBox txtServerPort;
        private Button btnConnect;
        private Label lblConnectionStatus;
        private TextBox txtAgentLog;
        private ListBox listBoxTasks;
        private Label lblAgentStatus;
        private ProgressBar progressBar;
        private Label lblProgress;
        private Label lblSpeed;
        private Label lblCurrentTask;
        private Button btnClearLog;
        private Button btnCheckPort;
        private NumericUpDown numThreads;
        private TextBox txtAgentName;
        private const string ConfigFile = "agent_config.json";

        public AgentForm()
        {
            InitializeComponent();
            LoadAgentConfig();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(1200, 800);
            this.Text = "Bitcoin Finder - Режим агента";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(1000, 600);

            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 4;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Подключение
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Статус
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F)); // Задания
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F)); // Лог

            // === СЕКЦИЯ ПОДКЛЮЧЕНИЯ ===
            var connectionGroup = new GroupBox();
            connectionGroup.Text = "Подключение к серверу";
            connectionGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            connectionGroup.Dock = DockStyle.Fill;

            var connectionLayout = new TableLayoutPanel();
            connectionLayout.Dock = DockStyle.Fill;
            connectionLayout.ColumnCount = 4;
            connectionLayout.RowCount = 3; // Increased row count for new controls
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

            // IP сервера
            connectionLayout.Controls.Add(new Label { Text = "IP сервера:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            txtServerIp = new TextBox { Text = "127.0.0.1", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(txtServerIp, 1, 0);

            // Порт сервера
            connectionLayout.Controls.Add(new Label { Text = "Порт:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 2, 0);
            txtServerPort = new TextBox { Text = "5000", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(txtServerPort, 3, 0);

            // Кнопка подключения
            btnConnect = new Button { Text = "Подключиться", Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.LightGreen, Dock = DockStyle.Fill };
            btnConnect.Click += BtnConnect_Click;
            connectionLayout.Controls.Add(btnConnect, 0, 1);

            // Статус подключения
            lblConnectionStatus = new Label { Text = "Статус: Отключено", Font = new Font("Segoe UI", 10F), ForeColor = Color.Red, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            connectionLayout.Controls.Add(lblConnectionStatus, 1, 1);
            connectionLayout.SetColumnSpan(lblConnectionStatus, 3);

            // Кнопка проверки порта
            btnCheckPort = new Button { Text = "Проверить порт", Font = new Font("Segoe UI", 9F), Dock = DockStyle.Fill };
            btnCheckPort.Click += BtnCheckPort_Click;
            connectionLayout.Controls.Add(btnCheckPort, 2, 1);

            // Добавляем поле для имени агента
            connectionLayout.Controls.Add(new Label { Text = "Имя агента:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            txtAgentName = new TextBox { Text = Environment.MachineName, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(txtAgentName, 1, 2);
            // Добавляем поле для потоков
            connectionLayout.Controls.Add(new Label { Text = "Потоков:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 2, 2);
            numThreads = new NumericUpDown { Minimum = 1, Maximum = 128, Value = 1, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(numThreads, 3, 2);

            connectionGroup.Controls.Add(connectionLayout);
            mainLayout.Controls.Add(connectionGroup, 0, 0);
            mainLayout.SetColumnSpan(connectionGroup, 2);

            // === СЕКЦИЯ СТАТУСА АГЕНТА ===
            var statusGroup = new GroupBox();
            statusGroup.Text = "Статус агента";
            statusGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            statusGroup.Dock = DockStyle.Fill;

            var statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.ColumnCount = 4;
            statusLayout.RowCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            // Статус агента
            lblAgentStatus = new Label { Text = "Статус: Не подключен", Font = new Font("Segoe UI", 10F), ForeColor = Color.Red, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblAgentStatus, 0, 0);

            // Прогресс
            lblProgress = new Label { Text = "Обработано: 0", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblProgress, 1, 0);

            // Скорость
            lblSpeed = new Label { Text = "Скорость: 0/сек", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblSpeed, 2, 0);

            // Текущее задание
            lblCurrentTask = new Label { Text = "Задание: Нет", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblCurrentTask, 3, 0);

            // Прогресс бар
            progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 20 };
            statusLayout.SetColumnSpan(progressBar, 4);
            statusLayout.Controls.Add(progressBar, 0, 1);

            statusGroup.Controls.Add(statusLayout);
            mainLayout.Controls.Add(statusGroup, 0, 1);
            mainLayout.SetColumnSpan(statusGroup, 2);

            // === СЕКЦИЯ ЗАДАНИЙ ===
            var tasksGroup = new GroupBox();
            tasksGroup.Text = "Текущие задания";
            tasksGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            tasksGroup.Dock = DockStyle.Fill;

            listBoxTasks = new ListBox();
            listBoxTasks.Dock = DockStyle.Fill;
            listBoxTasks.Font = new Font("Consolas", 10F);
            listBoxTasks.Items.Add("Ожидание подключения к серверу...");

            tasksGroup.Controls.Add(listBoxTasks);
            mainLayout.Controls.Add(tasksGroup, 0, 2);

            // === СЕКЦИЯ ЛОГА ===
            var logGroup = new GroupBox();
            logGroup.Text = "Лог работы агента";
            logGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            logGroup.Dock = DockStyle.Fill;

            var logLayout = new TableLayoutPanel();
            logLayout.Dock = DockStyle.Fill;
            logLayout.ColumnCount = 1;
            logLayout.RowCount = 2;
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            txtAgentLog = new TextBox();
            txtAgentLog.Dock = DockStyle.Fill;
            txtAgentLog.Multiline = true;
            txtAgentLog.ScrollBars = ScrollBars.Vertical;
            txtAgentLog.Font = new Font("Consolas", 9F);
            txtAgentLog.ReadOnly = true;
            txtAgentLog.BackColor = Color.Black;
            txtAgentLog.ForeColor = Color.Lime;
            logLayout.Controls.Add(txtAgentLog, 0, 0);

            // Кнопка очистки лога
            btnClearLog = new Button { Text = "Очистить лог", Font = new Font("Segoe UI", 9F), Dock = DockStyle.Fill };
            btnClearLog.Click += BtnClearLog_Click;
            logLayout.Controls.Add(btnClearLog, 0, 1);

            logGroup.Controls.Add(logLayout);
            mainLayout.Controls.Add(logGroup, 0, 3);

            this.Controls.Add(mainLayout);

            // Инициализация
            AddLog("=== РЕЖИМ АГЕНТА ЗАПУЩЕН ===");
            AddLog("Для начала работы подключитесь к серверу координатору");
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (agentController?.IsConnected == true)
            {
                await DisconnectFromServer();
            }
            else
            {
                await ConnectToServer();
            }
        }

        private async Task ConnectToServer()
        {
            // Валидация
            string ip = txtServerIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("Введите IP адрес сервера!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(txtServerPort.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Введите корректный номер порта (1-65535)!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Сохраняем параметры
            SaveAgentConfig();

            try
            {
                // Создаем контроллер агента
                agentController = new AgentController(this);
                agentController.OnStatusChanged += status => Invoke(new Action(() => UpdateAgentStatus(status)));
                agentController.OnLog += log => Invoke(new Action(() => AddAgentLog(log)));
                agentController.OnTaskReceived += task => Invoke(new Action(() => UpdateTaskInfo(task)));
                agentController.OnProgressUpdate += (current, rate) => Invoke(new Action(() => UpdateAgentProgress(current, rate)));

                btnConnect.Enabled = false;
                btnConnect.Text = "Подключение...";

                // Подключаемся к серверу с параметрами агента
                string agentName = txtAgentName.Text.Trim();
                int threads = (int)numThreads.Value;
                bool connected = await agentController.ConnectAsync(ip, port, agentName, threads);
                
                if (connected)
                {
                    btnConnect.Text = "Отключиться";
                    btnConnect.BackColor = Color.LightCoral;
                    AddLog($"Успешно подключен к серверу {ip}:{port}");
                }
                else
                {
                    btnConnect.Text = "Подключиться";
                    btnConnect.BackColor = Color.LightGreen;
                    agentController?.Dispose();
                    agentController = null;
                    AddLog("Не удалось подключиться к серверу");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnConnect.Text = "Подключиться";
                btnConnect.BackColor = Color.LightGreen;
                agentController?.Dispose();
                agentController = null;
                AddLog($"Ошибка подключения: {ex.Message}");
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private async Task DisconnectFromServer()
        {
            try
            {
                btnConnect.Enabled = false;
                btnConnect.Text = "Отключение...";

                if (agentController != null)
                {
                    await agentController.DisconnectAsync();
                    agentController.Dispose();
                    agentController = null;
                }

                btnConnect.Text = "Подключиться";
                btnConnect.BackColor = Color.LightGreen;
                
                // Очищаем агентский интерфейс
                listBoxTasks.Items.Clear();
                listBoxTasks.Items.Add("Отключено от сервера");
                AddLog("=== ОТКЛЮЧЕНО ОТ СЕРВЕРА ===");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отключения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AddLog($"Ошибка отключения: {ex.Message}");
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private void UpdateAgentStatus(string status)
        {
            lblConnectionStatus.Text = $"Статус: {status}";
            
            if (status.Contains("Подключено"))
            {
                lblConnectionStatus.ForeColor = Color.Green;
                lblAgentStatus.Text = $"Статус: {status}";
                lblAgentStatus.ForeColor = Color.Green;
            }
            else if (status.Contains("Подключение") || status.Contains("Ожидание"))
            {
                lblConnectionStatus.ForeColor = Color.Orange;
                lblAgentStatus.Text = $"Статус: {status}";
                lblAgentStatus.ForeColor = Color.Orange;
            }
            else
            {
                lblConnectionStatus.ForeColor = Color.Red;
                lblAgentStatus.Text = $"Статус: {status}";
                lblAgentStatus.ForeColor = Color.Red;
            }
        }

        private void AddAgentLog(string log)
        {
            AddLog(log);
        }

        private void AddLog(string message)
        {
            if (txtAgentLog.InvokeRequired)
            {
                txtAgentLog.Invoke(new Action(() => AddLog(message)));
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{timestamp}] {message}";
            txtAgentLog.AppendText(logMessage + "\r\n");
            txtAgentLog.SelectionStart = txtAgentLog.Text.Length;
            txtAgentLog.ScrollToCaret();
            
            // Логируем в файл
            // Logger.LogAgent(message); // Закомментировано для уменьшения мусора в логах
        }

        private void UpdateTaskInfo(AgentTaskInfo task)
        {
            listBoxTasks.Items.Clear();
            listBoxTasks.Items.Add($"Обрабатываем блок {task.BlockId}");
            listBoxTasks.Items.Add($"Диапазон: {task.StartIndex:N0} - {task.EndIndex:N0}");
            listBoxTasks.Items.Add($"Комбинаций: {task.EstimatedCombinations:N0}");
            listBoxTasks.Items.Add($"Начато: {task.StartTime:HH:mm:ss}");
            
            lblCurrentTask.Text = $"Задание: Блок {task.BlockId}";
            AddLog($"Получено задание: блок {task.BlockId} ({task.EstimatedCombinations:N0} комбинаций)");
        }

        private void UpdateAgentProgress(long current, double rate)
        {
            lblProgress.Text = $"Обработано: {current:N0}";
            lblSpeed.Text = $"Скорость: {rate:F0}/сек";
            
            // Обновляем прогресс бар (если есть общее количество)
            if (rate > 0)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }
        }

        private void BtnClearLog_Click(object? sender, EventArgs e)
        {
            txtAgentLog.Clear();
            AddLog("Лог очищен");
        }

        private async void BtnCheckPort_Click(object? sender, EventArgs e)
        {
            string ip = txtServerIp.Text.Trim();
            if (!int.TryParse(txtServerPort.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Введите корректный номер порта (1-65535)!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AddLog($"Проверка порта {ip}:{port}...");
            bool open = await IsPortOpenAsync(ip, port, 2000);
            if (open)
            {
                AddLog($"Порт {ip}:{port} ОТКРЫТ (доступен для подключения)");
                lblConnectionStatus.Text = $"Порт {ip}:{port} открыт";
                lblConnectionStatus.ForeColor = Color.Green;
            }
            else
            {
                AddLog($"Порт {ip}:{port} ЗАКРЫТ или недоступен");
                lblConnectionStatus.Text = $"Порт {ip}:{port} закрыт";
                lblConnectionStatus.ForeColor = Color.Red;
            }
        }

        private async Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    var timeoutTask = Task.Delay(timeoutMs);
                    var completed = await Task.WhenAny(connectTask, timeoutTask);
                    return connectTask.IsCompletedSuccessfully && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        private void SaveAgentConfig()
        {
            var config = new AgentConfig
            {
                ServerIp = txtServerIp.Text.Trim(),
                ServerPort = txtServerPort.Text.Trim(),
                AgentName = txtAgentName.Text.Trim(),
                Threads = (int)numThreads.Value
            };
            try
            {
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config));
            }
            catch { }
        }

        private void LoadAgentConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(ConfigFile));
                    if (config != null)
                    {
                        txtServerIp.Text = config.ServerIp;
                        txtServerPort.Text = config.ServerPort;
                        txtAgentName.Text = config.AgentName;
                        numThreads.Value = Math.Max(1, Math.Min(numThreads.Maximum, config.Threads));
                        
                        // Проверяем наличие сохраненного прогресса
                        if (File.Exists("agent_config.json"))
                        {
                            var progressJson = File.ReadAllText("agent_config.json");
                            var progress = JsonSerializer.Deserialize<Dictionary<string, object>>(progressJson);
                            if (progress != null && progress.ContainsKey("LastBlockId") && progress.ContainsKey("LastIndex"))
                            {
                                var lastBlockId = Convert.ToInt32(progress["LastBlockId"]);
                                var lastIndex = Convert.ToInt64(progress["LastIndex"]);
                                if (lastBlockId >= 0 && lastIndex > 0)
                                {
                                    AddLog($"Найден сохраненный прогресс: блок {lastBlockId}, позиция {lastIndex:N0}");
                                    AddLog("Агент будет запрашивать продолжение с этой позиции");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка загрузки конфигурации: {ex.Message}");
            }
        }

        private class AgentConfig
        {
            public string ServerIp { get; set; } = "127.0.0.1";
            public string ServerPort { get; set; } = "5000";
            public string AgentName { get; set; } = "";
            public int Threads { get; set; } = 1;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (agentController?.IsConnected == true)
            {
                _ = Task.Run(async () => await agentController.DisconnectAsync());
            }
            
            agentController?.Dispose();
            base.OnFormClosing(e);
        }
    }
} 