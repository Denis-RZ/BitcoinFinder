using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation; // Added for NetworkInterface
using System.Collections.Generic; // Added for List

namespace BitcoinFinder
{
    public partial class ServerForm : Form
    {
        private DistributedServer? server;
        private bool isServerRunning = false;
        private System.Windows.Forms.Timer statsTimer;
        
        // UI элементы
        private TextBox txtPort;
        private TextBox txtBitcoinAddress;
        private NumericUpDown numWordCount;
        private NumericUpDown numServerThreads;
        private TextBox txtBlockSize;
        private Button btnStartServer;
        private Button btnStopServer;
        private TextBox txtServerLog;
        private Label lblServerStatus;
        private DataGridView dgvAgents;
        private Label lblStats;
        private ListBox lstFoundResults;
        private ProgressBar progressBar;
        private Label lblServerIp;
        private FlowLayoutPanel legendPanel; // Добавить панель для легенды

        // Добавить словарь для цветов агентов
        private Dictionary<string, Color> agentColors = new Dictionary<string, Color>();
        private Color[] palette = new Color[]
        {
            Color.FromArgb(220,255,220), Color.FromArgb(220,240,255), Color.FromArgb(255,245,200),
            Color.FromArgb(255,220,220), Color.FromArgb(220,255,255), Color.FromArgb(255,220,255),
            Color.FromArgb(240,220,255), Color.FromArgb(255,240,220), Color.FromArgb(220,255,240),
            Color.FromArgb(240,255,220), Color.FromArgb(220,220,255), Color.FromArgb(255,220,240)
        };
        private int colorIndex = 0;

        public ServerForm()
        {
            // Цвета для светлой пастельной темы
            Color pastelBlue = Color.FromArgb(230, 240, 255);
            Color pastelGreen = Color.FromArgb(220, 255, 230);
            Color pastelRed = Color.FromArgb(255, 230, 230);
            Color pastelGray = Color.FromArgb(245, 245, 250);
            Color pastelAccent = Color.FromArgb(220, 230, 255);
            Color pastelYellow = Color.FromArgb(255, 255, 220);
            Color pastelBorder = Color.FromArgb(210, 220, 230);

            // Инициализируем UI элементы перед вызовом InitializeComponent
            txtPort = new TextBox();
            txtBitcoinAddress = new TextBox();
            numWordCount = new NumericUpDown();
            numServerThreads = new NumericUpDown();
            txtBlockSize = new TextBox();
            btnStartServer = new Button();
            btnStopServer = new Button();
            txtServerLog = new TextBox();
            lblServerStatus = new Label();
            dgvAgents = new DataGridView();
            lblStats = new Label();
            lstFoundResults = new ListBox();
            progressBar = new ProgressBar();
            lblServerIp = new Label();
            legendPanel = new FlowLayoutPanel();
            statsTimer = new System.Windows.Forms.Timer();

            // ToolTip для всех важных элементов
            var toolTip = new ToolTip();
            toolTip.SetToolTip(btnStartServer, "Запустить сервер");
            toolTip.SetToolTip(btnStopServer, "Остановить сервер");
            toolTip.SetToolTip(txtPort, "Порт для подключения агентов");
            toolTip.SetToolTip(txtBitcoinAddress, "Целевой Bitcoin адрес");
            toolTip.SetToolTip(numWordCount, "Количество слов в seed-фразе");
            toolTip.SetToolTip(txtBlockSize, "Размер блока для обработки");
            toolTip.SetToolTip(numServerThreads, "Количество потоков сервера");

            // Цвета и стили для кнопок
            btnStartServer.FlatStyle = FlatStyle.Flat;
            btnStartServer.BackColor = pastelGreen;
            btnStartServer.ForeColor = Color.DarkGreen;
            btnStartServer.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnStartServer.FlatAppearance.BorderColor = pastelBorder;
            btnStartServer.FlatAppearance.BorderSize = 1;
            btnStartServer.Text = "▶️ Запустить";
            btnStartServer.Height = 40;

            btnStopServer.FlatStyle = FlatStyle.Flat;
            btnStopServer.BackColor = pastelRed;
            btnStopServer.ForeColor = Color.DarkRed;
            btnStopServer.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnStopServer.FlatAppearance.BorderColor = pastelBorder;
            btnStopServer.FlatAppearance.BorderSize = 1;
            btnStopServer.Text = "⏹️ Остановить";
            btnStopServer.Height = 40;

            // Стиль для DataGridView
            dgvAgents.BackgroundColor = pastelGray;
            dgvAgents.DefaultCellStyle.BackColor = pastelGray;
            dgvAgents.DefaultCellStyle.SelectionBackColor = pastelAccent;
            dgvAgents.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgvAgents.DefaultCellStyle.Font = new Font("Segoe UI", 10F);
            dgvAgents.ColumnHeadersDefaultCellStyle.BackColor = pastelBlue;
            dgvAgents.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            dgvAgents.EnableHeadersVisualStyles = false;
            dgvAgents.GridColor = pastelBorder;
            dgvAgents.BorderStyle = BorderStyle.None;
            dgvAgents.RowHeadersVisible = false;
            dgvAgents.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvAgents.AllowUserToResizeRows = false;
            dgvAgents.AllowUserToResizeColumns = false;

            // Стиль для логов
            txtServerLog.BackColor = Color.FromArgb(30, 30, 30);
            txtServerLog.ForeColor = Color.FromArgb(180, 255, 180);
            txtServerLog.Font = new Font("Consolas", 10F);
            txtServerLog.BorderStyle = BorderStyle.FixedSingle;

            // Стиль для прогресс-бара
            progressBar.Height = 30;
            progressBar.ForeColor = Color.FromArgb(120, 200, 120);
            progressBar.BackColor = pastelGray;

            // Стиль для GroupBox
            this.BackColor = pastelGray;

            // Заголовок формы
            this.Text = "🖥️ Bitcoin Finder Server";
            this.Font = new Font("Segoe UI", 11F);

            // Цвета для лейаутов и панелей
            // (остальной код инициализации UI ниже не меняется)

            Program.LoadConfig();
            InitializeComponent();
            SetupStatsTimer();
            LoadServerConfig();
            string serverIp = GetLocalIPAddress();
            int port = Program.Config.Server.Port;
            lblServerIp.Text = $"🌐 IP для агентов: {serverIp}:{port}";
            lblServerIp.ForeColor = Color.DarkGreen;
            this.FormClosing += ServerForm_FormClosing;
        }

        private void InitializeComponent()
        {
            this.Size = new Size(1400, 900);
            this.Text = "Bitcoin Finder - Distributed Server";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(1200, 700);

            // === ГЛАВНАЯ РАЗМЕТКА ===
            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 4;
            
            // Столбцы: 35% левый, 65% правый
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            
            // Строки: заголовок, конфигурация, агенты/прогресс, логи
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F)); // Заголовок с IP
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F)); // Конфигурация
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40F)); // Агенты и прогресс
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F)); // Логи

            // === ЗАГОЛОВОК С IP АДРЕСОМ ===
            var headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.BackColor = Color.FromArgb(240, 248, 255); // AliceBlue
            headerPanel.BorderStyle = BorderStyle.FixedSingle;

            var headerLayout = new TableLayoutPanel();
            headerLayout.Dock = DockStyle.Fill;
            headerLayout.ColumnCount = 2;
            headerLayout.RowCount = 2;
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // Заголовок сервера
            var lblServerTitle = new Label();
            lblServerTitle.Text = "🖥️ Bitcoin Finder Server";
            lblServerTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            lblServerTitle.ForeColor = Color.DarkBlue;
            lblServerTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblServerTitle.Dock = DockStyle.Fill;
            headerLayout.Controls.Add(lblServerTitle, 0, 0);

            // IP адрес сервера (ВАЖНО!)
            lblServerIp = new Label();
            lblServerIp.Text = "🌐 IP для агентов: Определяется...";
            lblServerIp.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblServerIp.ForeColor = Color.DarkGreen;
            lblServerIp.TextAlign = ContentAlignment.MiddleRight;
            lblServerIp.Dock = DockStyle.Fill;
            lblServerIp.AutoSize = false;
            headerLayout.Controls.Add(lblServerIp, 1, 0);

            // Статус сервера
            lblServerStatus = new Label();
            lblServerStatus.Text = "🔴 Статус: Остановлен";
            lblServerStatus.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblServerStatus.ForeColor = Color.Red;
            lblServerStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblServerStatus.Dock = DockStyle.Fill;
            headerLayout.Controls.Add(lblServerStatus, 0, 1);

            // Кнопки управления (в одной строке)
            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.LeftToRight;
            buttonPanel.WrapContents = false;

            btnStartServer = new Button();
            btnStartServer.Text = "▶️ Запустить сервер";
            btnStartServer.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnStartServer.BackColor = Color.LightGreen;
            btnStartServer.ForeColor = Color.DarkGreen;
            btnStartServer.Size = new Size(150, 35);
            btnStartServer.FlatStyle = FlatStyle.Flat;
            btnStartServer.Click += BtnStartServer_Click;
            buttonPanel.Controls.Add(btnStartServer);

            btnStopServer = new Button();
            btnStopServer.Text = "⏹️ Остановить сервер";
            btnStopServer.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnStopServer.BackColor = Color.LightCoral;
            btnStopServer.ForeColor = Color.DarkRed;
            btnStopServer.Size = new Size(150, 35);
            btnStopServer.Enabled = false;
            btnStopServer.FlatStyle = FlatStyle.Flat;
            btnStopServer.Click += BtnStopServer_Click;
            buttonPanel.Controls.Add(btnStopServer);

            headerLayout.Controls.Add(buttonPanel, 1, 1);
            headerPanel.Controls.Add(headerLayout);
            mainLayout.Controls.Add(headerPanel, 0, 0);
            mainLayout.SetColumnSpan(headerPanel, 2);

            // === КОНФИГУРАЦИЯ СЕРВЕРА ===
            var configGroup = new GroupBox();
            configGroup.Text = "⚙️ Конфигурация сервера";
            configGroup.Dock = DockStyle.Fill;
            configGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            var configLayout = new TableLayoutPanel();
            configLayout.Dock = DockStyle.Fill;
            configLayout.ColumnCount = 2;
            configLayout.RowCount = 6;
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            configLayout.Padding = new Padding(10);

            // Порт сервера
            configLayout.Controls.Add(new Label { Text = "🔌 Порт:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            txtPort = new TextBox { Text = "5000", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            configLayout.Controls.Add(txtPort, 1, 0);

            // Bitcoin адрес
            configLayout.Controls.Add(new Label { Text = "₿ Bitcoin адрес:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            txtBitcoinAddress = new TextBox { Text = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            configLayout.Controls.Add(txtBitcoinAddress, 1, 1);

            // Количество слов
            configLayout.Controls.Add(new Label { Text = "📝 Количество слов:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            numWordCount = new NumericUpDown { Value = 12, Minimum = 12, Maximum = 24, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            configLayout.Controls.Add(numWordCount, 1, 2);

            // Размер блока
            configLayout.Controls.Add(new Label { Text = "📦 Размер блока:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 3);
            txtBlockSize = new TextBox { Text = "100000", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            configLayout.Controls.Add(txtBlockSize, 1, 3);

            // Потоков сервера
            configLayout.Controls.Add(new Label { Text = "⚡ Потоков сервера:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 4);
            numServerThreads = new NumericUpDown { Value = 2, Minimum = 0, Maximum = 16, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            configLayout.Controls.Add(numServerThreads, 1, 4);

            // Статистика
            lblStats = new Label();
            lblStats.Text = "📊 Агентов: 0 | Блоков: 0 | Обработано: 0";
            lblStats.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblStats.ForeColor = Color.DarkBlue;
            lblStats.Dock = DockStyle.Fill;
            lblStats.TextAlign = ContentAlignment.MiddleCenter;
            configLayout.Controls.Add(lblStats, 0, 5);
            configLayout.SetColumnSpan(lblStats, 2);

            configGroup.Controls.Add(configLayout);
            mainLayout.Controls.Add(configGroup, 0, 1);

            // === НАЙДЕННЫЕ РЕЗУЛЬТАТЫ ===
            var resultsGroup = new GroupBox();
            resultsGroup.Text = "🎯 Найденные результаты";
            resultsGroup.Dock = DockStyle.Fill;
            resultsGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            lstFoundResults = new ListBox();
            lstFoundResults.Dock = DockStyle.Fill;
            lstFoundResults.Font = new Font("Consolas", 9F);
            lstFoundResults.HorizontalScrollbar = true;
            lstFoundResults.BackColor = Color.Black;
            lstFoundResults.ForeColor = Color.Yellow;
            resultsGroup.Controls.Add(lstFoundResults);
            
            mainLayout.Controls.Add(resultsGroup, 1, 1);

            // === ПОДКЛЮЧЕННЫЕ АГЕНТЫ ===
            var agentsGroup = new GroupBox();
            agentsGroup.Text = "🤖 Подключенные агенты";
            agentsGroup.Dock = DockStyle.Fill;
            agentsGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            dgvAgents = new DataGridView();
            dgvAgents.Dock = DockStyle.Fill;
            dgvAgents.AutoGenerateColumns = false;
            dgvAgents.AllowUserToAddRows = false;
            dgvAgents.AllowUserToDeleteRows = false;
            dgvAgents.ReadOnly = true;
            dgvAgents.Font = new Font("Segoe UI", 9F);
            dgvAgents.BackgroundColor = Color.White;
            dgvAgents.GridColor = Color.LightGray;
            
            // Колонки для агентов
            dgvAgents.Columns.Add(new DataGridViewTextBoxColumn { Name = "AgentId", HeaderText = "ID Агента", Width = 150 });
            dgvAgents.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProcessedCount", HeaderText = "Обработано", Width = 100 });
            dgvAgents.Columns.Add(new DataGridViewTextBoxColumn { Name = "CurrentRate", HeaderText = "Скорость/сек", Width = 100 });
            dgvAgents.Columns.Add(new DataGridViewTextBoxColumn { Name = "CompletedBlocks", HeaderText = "Блоков", Width = 80 });
            dgvAgents.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastUpdate", HeaderText = "Последняя активность", Width = 150 });

            // Добавляем обработчик для корректного копирования
            dgvAgents.KeyDown += DgvAgents_KeyDown;

            agentsGroup.Controls.Add(dgvAgents);
            mainLayout.Controls.Add(agentsGroup, 0, 2);

            // === ПРОГРЕСС ===
            var progressGroup = new GroupBox();
            progressGroup.Text = "📈 Общий прогресс";
            progressGroup.Dock = DockStyle.Fill;
            progressGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            var progressLayout = new TableLayoutPanel();
            progressLayout.Dock = DockStyle.Fill;
            progressLayout.RowCount = 2;
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Height = 30;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressLayout.Controls.Add(progressBar, 0, 0);

            var progressLabel = new Label();
            progressLabel.Text = "0%";
            progressLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            progressLabel.TextAlign = ContentAlignment.MiddleCenter;
            progressLabel.Dock = DockStyle.Fill;
            progressLabel.ForeColor = Color.DarkBlue;
            progressLayout.Controls.Add(progressLabel, 0, 1);

            progressGroup.Controls.Add(progressLayout);
            mainLayout.Controls.Add(progressGroup, 1, 2);

            // === ЛОГ СЕРВЕРА ===
            var logGroup = new GroupBox();
            logGroup.Text = "📋 Лог сервера";
            logGroup.Dock = DockStyle.Fill;
            logGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            txtServerLog = new TextBox();
            txtServerLog.Multiline = true;
            txtServerLog.ScrollBars = ScrollBars.Vertical;
            txtServerLog.Dock = DockStyle.Fill;
            txtServerLog.Font = new Font("Consolas", 9F);
            txtServerLog.ReadOnly = true;
            txtServerLog.BackColor = Color.Black;
            txtServerLog.ForeColor = Color.LightGreen;

            logGroup.Controls.Add(txtServerLog);
            mainLayout.Controls.Add(logGroup, 0, 3);
            mainLayout.SetColumnSpan(logGroup, 2);

            // === ЛЕГЕНДА ===
            legendPanel.Dock = DockStyle.Fill;
            legendPanel.FlowDirection = FlowDirection.TopDown;
            legendPanel.WrapContents = false;
            legendPanel.AutoScroll = true; // Разрешить прокрутку, если элементов много
            mainLayout.Controls.Add(legendPanel, 1, 3); // Поместить легенду в последнюю строку

            Controls.Add(mainLayout);
        }

        private void SetupStatsTimer()
        {
            statsTimer = new System.Windows.Forms.Timer();
            statsTimer.Interval = 5000; // 5 секунд
            statsTimer.Tick += StatsTimer_Tick;
        }

        private async void BtnStartServer_Click(object? sender, EventArgs e)
        {
            if (isServerRunning) return;

            try
            {
                // Валидация параметров
                if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Введите корректный номер порта (1-65535)!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string bitcoinAddress = txtBitcoinAddress.Text.Trim();
                if (string.IsNullOrWhiteSpace(bitcoinAddress))
                {
                    MessageBox.Show("Введите Bitcoin адрес!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int wordCount = (int)numWordCount.Value;
                // Парсим размер блока
                long blockSize = Program.Config.Server.BlockSize;
                if (!long.TryParse(txtBlockSize.Text.Trim(), out blockSize) || blockSize <= 0)
                {
                    MessageBox.Show("Введите корректный размер блока (целое положительное число)!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
               
                // Получаем количество потоков сервера
                int serverThreads = (int)numServerThreads.Value;
                
                // Сохраняем конфигурацию перед запуском
                SaveServerConfig();
                
                // Создаем и запускаем сервер
                server = new DistributedServer(port)
                {
                    BlockSize = blockSize // применяем размер блока
                };
                server.OnLog += Server_OnLog;
                server.OnFoundResult += Server_OnFoundResult;
                server.OnStatsUpdate += Server_OnStatsUpdate;

                // Запускаем сервер в фоновой задаче
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await server.StartAsync(bitcoinAddress, wordCount, null, serverThreads > 0, serverThreads);
                    }
                    catch (Exception ex)
                    {
                        Invoke(new Action(() =>
                        {
                            MessageBox.Show($"Ошибка запуска сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            BtnStopServer_Click(this, EventArgs.Empty);
                        }));
                    }
                });

                // Обновляем UI
                isServerRunning = true;
                btnStartServer.Enabled = false;
                btnStopServer.Enabled = true;
                lblServerStatus.Text = $"🟢 Статус: Запущен на порту {port}";
                lblServerStatus.ForeColor = Color.Green;
                
                // Определяем и отображаем IP адрес сервера
                string serverIp = GetLocalIPAddress();
                lblServerIp.Text = $"🌐 IP для агентов: {serverIp}:{port}";
                lblServerIp.ForeColor = Color.Green;
                
                // Блокируем изменение параметров
                txtPort.Enabled = false;
                txtBitcoinAddress.Enabled = false;
                numWordCount.Enabled = false;
                numServerThreads.Enabled = false;
                txtBlockSize.Enabled = false;

                statsTimer.Start();
                
                AddLog($"Сервер запущен на порту {port}");
                AddLog($"IP адрес сервера для агентов: {serverIp}:{port}");
                AddLog($"Целевой адрес: {bitcoinAddress}");
                AddLog($"Количество слов: {wordCount}");
                AddLog($"Потоков сервера: {serverThreads} (0 = отключено)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStopServer_Click(object? sender, EventArgs e)
        {
            if (!isServerRunning) return;

            try
            {
                server?.Stop();
                server = null;

                isServerRunning = false;
                btnStartServer.Enabled = true;
                btnStopServer.Enabled = false;
                lblServerStatus.Text = "🔴 Статус: Остановлен";
                lblServerStatus.ForeColor = Color.Red;
                // Показываем IP даже после остановки
                string serverIp = GetLocalIPAddress();
                int port = Program.Config.Server.Port;
                lblServerIp.Text = $"🌐 IP для агентов: {serverIp}:{port}";
                lblServerIp.ForeColor = Color.DarkGreen;

                // Разблокируем параметры
                txtPort.Enabled = true;
                txtBitcoinAddress.Enabled = true;
                numWordCount.Enabled = true;
                numServerThreads.Enabled = true;
                txtBlockSize.Enabled = true;

                statsTimer.Stop();
                
                // Очищаем данные
                dgvAgents.Rows.Clear();
                lblStats.Text = "📊 Агентов: 0 | Блоков: 0 | Обработано: 0";
                progressBar.Value = 0;

                AddLog("Сервер остановлен");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Server_OnLog(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(Server_OnLog), message);
                return;
            }

            AddLog(message);
        }

        private void Server_OnFoundResult(string result)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(Server_OnFoundResult), result);
                return;
            }

            lstFoundResults.Items.Add($"[{DateTime.Now:HH:mm:ss}] {result}");
            if (lstFoundResults.Items.Count > 100) // Ограничиваем количество элементов
            {
                lstFoundResults.Items.RemoveAt(0);
            }

            // Показываем сообщение пользователю
            MessageBox.Show("Найдено совпадение! Проверьте раздел 'Найденные результаты'.", "Успех!", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Server_OnStatsUpdate(ServerStats stats)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<ServerStats>(Server_OnStatsUpdate), stats);
                return;
            }

            AddLog($"[UI] Получено обновление статистики: {stats.AgentStats.Count} агентов");
            UpdateStatsDisplay(stats);
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            if (server != null && isServerRunning)
            {
                var stats = server.GetCurrentStats();
                UpdateStatsDisplay(stats);
            }
        }

        private void UpdateStatsDisplay(ServerStats stats)
        {
            // Обновляем общую статистику
            lblStats.Text = $"📊 Агентов: {stats.ConnectedAgents} | " +
                           $"Блоков (ожидает/назначено/завершено): {stats.PendingBlocks}/{stats.AssignedBlocks}/{stats.CompletedBlocks} | " +
                           $"Обработано: {stats.TotalProcessed:N0} | " +
                           $"Найдено: {stats.FoundResults}";

            // Обновляем прогресс-бар
            if (stats.TotalCombinations > 0)
            {
                double percent = (double)stats.TotalProcessed / stats.TotalCombinations * 100;
                progressBar.Value = Math.Min((int)percent, 100);
                
                // Обновляем label прогресса
                var progressLabel = progressBar.Parent?.Controls.OfType<Label>().FirstOrDefault();
                if (progressLabel != null)
                {
                    progressLabel.Text = $"{percent:F2}% ({stats.TotalProcessed:N0} / {stats.TotalCombinations:N0})";
                }
            }

            // Обновляем таблицу агентов
            dgvAgents.Rows.Clear();
            AddLog($"[UI] Обновляем грид агентов: {stats.AgentStats.Count} агентов");
            
            foreach (var agentStat in stats.AgentStats)
            {
                AddLog($"[UI] Добавляем агента в грид: {agentStat.AgentId}, обработано: {agentStat.ProcessedCount:N0}, скорость: {agentStat.CurrentRate:F1}, блоков: {agentStat.CompletedBlocks}");
                dgvAgents.Rows.Add(
                    agentStat.AgentId,
                    agentStat.ProcessedCount.ToString("N0"),
                    agentStat.CurrentRate.ToString("F1"),
                    agentStat.CompletedBlocks,
                    agentStat.LastUpdate.ToString("HH:mm:ss")
                );
            }

            // Обновляем легенду
            UpdateAgentLegend();
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{timestamp}] {message}";
            txtServerLog.AppendText(logMessage + "\r\n");
            txtServerLog.SelectionStart = txtServerLog.Text.Length;
            txtServerLog.ScrollToCaret();

            // Ограничиваем размер лога
            if (txtServerLog.Lines.Length > 1000)
            {
                var lines = txtServerLog.Lines.Skip(500).ToArray();
                txtServerLog.Text = string.Join("\r\n", lines);
            }
            
            // Логируем в файл
            // Logger.LogServer(message); // Закомментировано для уменьшения мусора в логах
        }

        private void ServerForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (isServerRunning)
            {
                var result = MessageBox.Show("Сервер запущен. Остановить сервер и закрыть форму?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    BtnStopServer_Click(null, EventArgs.Empty);
                }
                else
                {
                    e.Cancel = true;
                    return;
                }
            }
            // Сохраняем конфиг при закрытии
            SaveServerConfig();
            Program.SaveConfig(); // Явное сохранение
        }
        
        private void LoadServerConfig()
        {
            try
            {
                // Загружаем серверные настройки из конфига
                txtPort.Text = Program.Config.Server.Port.ToString();
                txtBitcoinAddress.Text = !string.IsNullOrEmpty(Program.Config.Server.LastBitcoinAddress) 
                    ? Program.Config.Server.LastBitcoinAddress 
                    : Program.Config.DefaultBitcoinAddress;
                numWordCount.Value = Program.Config.Server.LastWordCount;
                txtBlockSize.Text = Program.Config.Server.BlockSize.ToString();
               
                // Загружаем количество потоков сервера (по умолчанию 2)
                numServerThreads.Value = Program.Config.DefaultThreadCount > 0 ? 
                    Math.Min(Program.Config.DefaultThreadCount / 2, 16) : 2; // Половина от общего числа потоков
                
                AddLog("Конфигурация сервера загружена");
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка загрузки конфигурации сервера: {ex.Message}");
            }
        }
        
        private void SaveServerConfig()
        {
            try
            {
                // Сохраняем серверные настройки в конфиг
                if (int.TryParse(txtPort.Text, out int port))
                    Program.Config.Server.Port = port;
                    
                Program.Config.Server.LastBitcoinAddress = txtBitcoinAddress.Text.Trim();
                Program.Config.Server.LastWordCount = (int)numWordCount.Value;
                
                if (long.TryParse(txtBlockSize.Text, out long blockSize))
                    Program.Config.Server.BlockSize = blockSize;
                
                // Сохраняем настройку потоков сервера (не добавляем в конфиг, используем только локально)
                // numServerThreads значение используется только во время сессии
                
                Program.SaveConfig();
                AddLog("Конфигурация сервера сохранена");
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка сохранения конфигурации сервера: {ex.Message}");
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                var ipList = new List<string>();
                foreach (var networkInterface in networkInterfaces)
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        var ipProperties = networkInterface.GetIPProperties();
                        foreach (var ipAddress in ipProperties.UnicastAddresses)
                        {
                            if (ipAddress.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(ipAddress.Address) &&
                                !ipAddress.Address.ToString().StartsWith("169.254."))
                            {
                                ipList.Add(ipAddress.Address.ToString());
                            }
                        }
                    }
                }
                if (ipList.Count > 0)
                    return string.Join(", ", ipList);
                return "127.0.0.1";
            }
            catch (Exception)
            {
                return "127.0.0.1";
            }
        }

        private void DgvAgents_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (dgvAgents.SelectedRows.Count > 0)
                {
                    string data = string.Join("\t", dgvAgents.SelectedRows.Cast<DataGridViewRow>()
                        .Select(row => string.Join("\t", row.Cells.Cast<DataGridViewCell>()
                            .Select(cell => cell.Value?.ToString() ?? ""))));
                    Clipboard.SetText(data);
                }
            }
        }

        private void UpdateAgentLegend()
        {
            // Очистить и пересоздать легенду
            legendPanel.Controls.Clear();
            int y = 0;
            // Получаем текущие статистики агентов и сервера
            var currentAgentStats = server?.GetCurrentStats().AgentStats.ToDictionary(a => a.AgentId) ?? new Dictionary<string, AgentStats>();
            // Добавляем агенты
            foreach (var agent in currentAgentStats.Values.OrderBy(a => a.AgentId))
            {
                if (!agentColors.ContainsKey(agent.AgentId))
                {
                    agentColors[agent.AgentId] = palette[colorIndex % palette.Length];
                    colorIndex++;
                }
                var color = agentColors[agent.AgentId];
                var panel = new Panel { BackColor = color, Width = 18, Height = 18, Left = 5, Top = y+2 };
                var label = new Label
                {
                    Text = $"{agent.AgentId}: {agent.ProcessedCount:N0} | {agent.CurrentRate:F1}/с | ETA: {FormatEta(agent.EtaSeconds)}",
                    Left = 28, Top = y, Width = 420, Height = 20, Font = new Font("Segoe UI", 9F)
                };
                legendPanel.Controls.Add(panel);
                legendPanel.Controls.Add(label);
                y += 22;
            }
            // Добавляем сервер
            var stats = server?.GetCurrentStats();
            if (stats != null)
            {
                if (!agentColors.ContainsKey("Сервер"))
                {
                    agentColors["Сервер"] = Color.LightGray; // Определяем цвет для сервера
                }
                var color = agentColors["Сервер"];
                var panel = new Panel { BackColor = color, Width = 18, Height = 18, Left = 5, Top = y+2 };
                var label = new Label
                {
                    Text = $"Сервер: {stats.TotalProcessed:N0} | ETA: ?",
                    Left = 28, Top = y, Width = 420, Height = 20, Font = new Font("Segoe UI", 9F)
                };
                legendPanel.Controls.Add(panel);
                legendPanel.Controls.Add(label);
                y += 22;
            }
        }
        private string FormatEta(double etaSeconds)
        {
            if (etaSeconds < 0 || double.IsInfinity(etaSeconds) || double.IsNaN(etaSeconds)) return "?";
            var ts = TimeSpan.FromSeconds(etaSeconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}ч {ts.Minutes}м {ts.Seconds}с";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}м {ts.Seconds}с";
            return $"{ts.Seconds}с";
        }
        // Вызовите UpdateAgentLegend() после обновления статистики агентов или сервера
    }
} 