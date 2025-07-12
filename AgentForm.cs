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
        private System.Windows.Forms.Timer? autosaveTimer;
        private bool isFormDisposed = false;
        private bool isLoadingConfig = false;

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
        private MaskedTextBox txtServerIpMasked;
        private NumericUpDown numServerPort;
        private const string ConfigFile = "agent_config.json";

        public AgentForm()
        {
            // Цвета для светлой пастельной темы
            Color pastelBlue = Color.FromArgb(230, 240, 255);
            Color pastelGreen = Color.FromArgb(220, 255, 230);
            Color pastelRed = Color.FromArgb(255, 230, 230);
            Color pastelGray = Color.FromArgb(245, 245, 250);
            Color pastelAccent = Color.FromArgb(220, 230, 255);
            Color pastelYellow = Color.FromArgb(255, 255, 220);
            Color pastelBorder = Color.FromArgb(210, 220, 230);

            var toolTip = new ToolTip();

            InitializeComponent();

            // ToolTip только для реально существующих контролов
            if (btnConnect != null)
                toolTip.SetToolTip(btnConnect, "Подключиться/отключиться к серверу");
            if (btnCheckPort != null)
                toolTip.SetToolTip(btnCheckPort, "Проверить доступность порта");
            if (txtServerIpMasked != null)
                toolTip.SetToolTip(txtServerIpMasked, "IP-адрес сервера");
            if (numServerPort != null)
                toolTip.SetToolTip(numServerPort, "Порт сервера");
            if (txtAgentName != null)
                toolTip.SetToolTip(txtAgentName, "Имя агента");
            if (numThreads != null)
                toolTip.SetToolTip(numThreads, "Количество потоков агента");

            // Цвета и стили для кнопки подключения
            btnConnect.FlatStyle = FlatStyle.Flat;
            btnConnect.BackColor = pastelGreen;
            btnConnect.ForeColor = Color.DarkGreen;
            btnConnect.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnConnect.FlatAppearance.BorderColor = pastelBorder;
            btnConnect.FlatAppearance.BorderSize = 1;
            btnConnect.Text = "🔗 Подключиться";
            btnConnect.Height = 40;

            // Стиль для кнопки проверки порта
            btnCheckPort.FlatStyle = FlatStyle.Flat;
            btnCheckPort.BackColor = pastelBlue;
            btnCheckPort.ForeColor = Color.Navy;
            btnCheckPort.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnCheckPort.FlatAppearance.BorderColor = pastelBorder;
            btnCheckPort.FlatAppearance.BorderSize = 1;

            // Стиль для логов
            txtAgentLog.BackColor = Color.FromArgb(30, 30, 30);
            txtAgentLog.ForeColor = Color.FromArgb(180, 255, 180);
            txtAgentLog.Font = new Font("Consolas", 10F);
            txtAgentLog.BorderStyle = BorderStyle.FixedSingle;

            // Стиль для прогресс-бара
            progressBar.Height = 30;
            progressBar.ForeColor = Color.FromArgb(120, 200, 120);
            progressBar.BackColor = pastelGray;

            // Стиль для GroupBox
            this.BackColor = pastelGray;

            // Заголовок формы
            this.Text = "🤖 Bitcoin Finder Agent";
            this.Font = new Font("Segoe UI", 11F);

            // Цвета для лейаутов и панелей
            // (остальной код инициализации UI ниже не меняется)

            LoadAgentConfig();

            // Подписка на изменения полей с автоматическим сохранением (только после загрузки конфига!)
            txtServerIp.TextChanged += (s, e) => 
            {
                if (!isFormDisposed && !this.IsDisposed && !isLoadingConfig)
                {
                    SaveAgentConfig();
                    AddLog("IP сервера изменен");
                }
            };
            txtServerPort.TextChanged += (s, e) => 
            {
                if (!isFormDisposed && !this.IsDisposed && !isLoadingConfig)
                {
                    SaveAgentConfig();
                    AddLog("Порт сервера изменен");
                }
            };
            txtAgentName.TextChanged += (s, e) => 
            {
                if (!isFormDisposed && !this.IsDisposed && !isLoadingConfig)
                {
                    SaveAgentConfig();
                    AddLog("Имя агента изменено");
                }
            };
            numThreads.ValueChanged += (s, e) => 
            {
                if (!isFormDisposed && !this.IsDisposed && !isLoadingConfig)
                {
                    SaveAgentConfig();
                    AddLog($"Количество потоков изменено на {numThreads.Value}");
                }
            };

            // Таймер автосохранения прогресса
            autosaveTimer = new System.Windows.Forms.Timer();
            autosaveTimer.Interval = 10000; // 10 секунд
            autosaveTimer.Tick += (s, e) => 
            {
                if (!isFormDisposed && !this.IsDisposed)
                {
                    SaveAgentConfig();
                }
            };
            autosaveTimer.Start();
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

            // IP сервера (TextBox)
            connectionLayout.Controls.Add(new Label { Text = "IP сервера:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            txtServerIp = new TextBox { Text = "127.0.0.1", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(txtServerIp, 1, 0);

            // Порт сервера (TextBox)
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
            numThreads = new NumericUpDown { Minimum = 1, Maximum = 128, Value = Environment.ProcessorCount, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
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

            if (!int.TryParse(txtServerPort.Text.Trim(), out int port) || port < 1 || port > 65535)
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
                
                // Сохраняем конфигурацию агента
                agentController.SaveAgentConfig();
                
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
            // Проверяем, не уничтожена ли форма
            if (isFormDisposed || this.IsDisposed || txtAgentLog.IsDisposed)
            {
                return;
            }

            if (txtAgentLog.InvokeRequired)
            {
                try
                {
                    txtAgentLog.Invoke(new Action(() => AddLog(message)));
                }
                catch (ObjectDisposedException)
                {
                    // Форма уже закрыта, игнорируем
                    return;
                }
                return;
            }

            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var logMessage = $"[{timestamp}] {message}";
                txtAgentLog.AppendText(logMessage + "\r\n");
                txtAgentLog.SelectionStart = txtAgentLog.Text.Length;
                txtAgentLog.ScrollToCaret();
                
                // Логируем в файл
                // Logger.LogAgent(message); // Закомментировано для уменьшения мусора в логах
            }
            catch (ObjectDisposedException)
            {
                // Форма уже закрыта, игнорируем
                return;
            }
            catch (Exception ex)
            {
                // Другие ошибки логируем в консоль
                System.Diagnostics.Debug.WriteLine($"Ошибка логирования: {ex.Message}");
            }
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
            if (!int.TryParse(txtServerPort.Text.Trim(), out int port) || port < 1 || port > 65535)
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
            if (isLoadingConfig) { AddLog("[DEBUG] SaveAgentConfig: вызов проигнорирован, идет загрузка"); return; }
            try
            {
                AddLog($"[DEBUG] SaveAgentConfig: ДО сохранения: IP={txtServerIp.Text}, Port={txtServerPort.Text}, Name={txtAgentName.Text}, Threads={numThreads.Value}");
                string ip = txtServerIp.Text.Trim();
                string portStr = txtServerPort.Text.Trim();
                string agentName = txtAgentName.Text.Trim();
                int threads = (int)numThreads.Value;
                if (!IsValidIpAddress(ip))
                {
                    AddLog($"Ошибка сохранения: некорректный IP адрес '{ip}'");
                    return;
                }
                if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
                {
                    AddLog($"Ошибка сохранения: некорректный порт '{portStr}'");
                    return;
                }
                if (string.IsNullOrWhiteSpace(agentName))
                    agentName = Environment.MachineName;
                if (threads < (int)numThreads.Minimum || threads > (int)numThreads.Maximum)
                    threads = Environment.ProcessorCount;
                var config = new AgentConfig
                {
                    ServerIp = ip,
                    ServerPort = port,
                    AgentName = agentName,
                    Threads = threads
                };
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                AddLog($"Сохранение конфигурации в файл: {Path.GetFullPath(ConfigFile)}");
                AddLog($"JSON для сохранения: {json}");
                File.WriteAllText(ConfigFile, json);
                if (File.Exists(ConfigFile))
                {
                    var savedContent = File.ReadAllText(ConfigFile);
                    AddLog($"Файл создан успешно. Размер: {savedContent.Length} байт");
                    AddLog($"Конфигурация сохранена: {config.ServerIp}:{config.ServerPort}, агент: {config.AgentName}, потоков: {config.Threads}");
                    AddLog($"[DEBUG] SaveAgentConfig: ПОСЛЕ сохранения: IP={txtServerIp.Text}, Port={txtServerPort.Text}, Name={txtAgentName.Text}, Threads={numThreads.Value}");
                }
                else
                {
                    AddLog("ОШИБКА: Файл не был создан!");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка сохранения конфигурации: {ex.Message}");
                AddLog($"Стек вызовов: {ex.StackTrace}");
            }
        }

        private bool IsValidIpAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            // Проверяем, является ли это localhost
            if (ip.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            // Проверяем формат IP адреса
            var parts = ip.Split('.');
            if (parts.Length != 4)
                return false;

            foreach (var part in parts)
            {
                if (!int.TryParse(part, out int num) || num < 0 || num > 255)
                    return false;
            }

            return true;
        }

        private void LoadAgentConfig()
        {
            isLoadingConfig = true;
            try
            {
                AddLog($"[DEBUG] LoadAgentConfig: ДО загрузки: IP={txtServerIp.Text}, Port={txtServerPort.Text}, Name={txtAgentName.Text}, Threads={numThreads.Value}");
                string ip = "127.0.0.1";
                int port = 5000;
                string agentName = Environment.MachineName;
                int threads = Environment.ProcessorCount;
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    AddLog($"Загрузка конфигурации из файла: {Path.GetFullPath(ConfigFile)}");
                    AddLog($"Содержимое файла: {json}");
                    var config = JsonSerializer.Deserialize<AgentConfig>(json);
                    if (config != null)
                    {
                        if (!string.IsNullOrWhiteSpace(config.ServerIp) && IsValidIpAddress(config.ServerIp))
                            ip = config.ServerIp;
                        else
                            AddLog($"Некорректный IP адрес в конфиге: '{config.ServerIp}', используем 127.0.0.1");
                        if (config.ServerPort >= 1 && config.ServerPort <= 65535)
                            port = config.ServerPort;
                        else
                            AddLog($"Некорректный порт в конфиге: {config.ServerPort}, используем 5000");
                        if (!string.IsNullOrWhiteSpace(config.AgentName))
                            agentName = config.AgentName;
                        if (config.Threads >= (int)numThreads.Minimum && config.Threads <= (int)numThreads.Maximum)
                            threads = config.Threads;
                        else
                            AddLog($"Некорректное число потоков в конфиге: {config.Threads}, используем {Environment.ProcessorCount}");
                    }
                }
                // Применяем значения к контролам
                txtServerIp.Text = ip;
                txtServerPort.Text = port.ToString();
                txtAgentName.Text = agentName;
                numThreads.Value = threads;
                AddLog($"[DEBUG] LoadAgentConfig: ПОСЛЕ загрузки: IP={txtServerIp.Text}, Port={txtServerPort.Text}, Name={txtAgentName.Text}, Threads={numThreads.Value}");
                AddLog($"Конфигурация загружена: {txtServerIp.Text}:{txtServerPort.Text}, агент: {txtAgentName.Text}, потоков: {numThreads.Value}");
                // Проверяем наличие сохраненного прогресса
                if (File.Exists("agent_progress.json"))
                {
                    try
                    {
                        var progressJson = File.ReadAllText("agent_progress.json");
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
                    catch (Exception ex)
                    {
                        AddLog($"Ошибка загрузки прогресса: {ex.Message}");
                    }
                }
            }
            catch (JsonException ex)
            {
                AddLog($"Ошибка десериализации конфигурации: {ex.Message}");
                AddLog("Удаляем битый файл конфигурации и создаем новый");
                
                try
                {
                    if (File.Exists(ConfigFile))
                        File.Delete(ConfigFile);
                }
                catch { }
                
                CreateDefaultConfig();
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка загрузки конфигурации: {ex.Message}");
                AddLog($"Стек вызовов: {ex.StackTrace}");
                CreateDefaultConfig();
            }
            finally
            {
                isLoadingConfig = false;
            }
        }

        private void CreateDefaultConfig()
        {
            isLoadingConfig = true;
            txtServerIp.Text = "127.0.0.1";
            txtServerPort.Text = "5000";
            txtAgentName.Text = Environment.MachineName;
            numThreads.Value = Environment.ProcessorCount;
            isLoadingConfig = false;
            SaveAgentConfig();
        }

        private class AgentConfig
        {
            public string ServerIp { get; set; } = "127.0.0.1";
            public int ServerPort { get; set; } = 5000; // Изменено с string на int
            public string AgentName { get; set; } = "";
            public int Threads { get; set; } = 1;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isFormDisposed = true; // Устанавливаем флаг перед уничтожением формы
            
            // Останавливаем таймер автосохранения
            if (autosaveTimer != null)
            {
                autosaveTimer.Stop();
                autosaveTimer.Dispose();
                autosaveTimer = null;
            }
            
            try
            {
                // Сохраняем конфигурацию без логирования
                SaveAgentConfigSilent();
            }
            catch (Exception ex)
            {
                // Логируем ошибку в консоль вместо UI
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения при закрытии: {ex.Message}");
            }
            
            if (agentController?.IsConnected == true)
            {
                try
                {
                    // Отключаемся от сервера без логирования
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await agentController.DisconnectAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Ошибка отключения: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при отключении: {ex.Message}");
                }
            }
            
            try
            {
                agentController?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при освобождении ресурсов: {ex.Message}");
            }
            
            base.OnFormClosing(e);
        }
        
        private void SaveAgentConfigSilent()
        {
            try
            {
                // Проверяем корректность данных перед сохранением
                string ip = txtServerIp.Text.Trim();
                string portStr = txtServerPort.Text.Trim();
                string agentName = txtAgentName.Text.Trim();
                int threads = (int)numThreads.Value;
                if (!IsValidIpAddress(ip))
                {
                    return; // Не логируем ошибки при закрытии
                }
                if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
                {
                    return; // Не логируем ошибки при закрытии
                }
                if (string.IsNullOrWhiteSpace(agentName))
                    agentName = Environment.MachineName;
                if (threads < (int)numThreads.Minimum || threads > (int)numThreads.Maximum)
                    threads = Environment.ProcessorCount;
                var config = new AgentConfig
                {
                    ServerIp = ip,
                    ServerPort = port,
                    AgentName = agentName,
                    Threads = threads
                };
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                // Логируем ошибку в консоль вместо UI
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения конфигурации: {ex.Message}");
            }
        }
    }
} 