using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace BitcoinFinder
{
    public class ServerForm : Form
    {
        private MenuStrip menuStrip;
        private ToolStripMenuItem fileMenu;
        private ToolStripMenuItem serverMenu;
        private ToolStripMenuItem helpMenu;
        private ToolStripMenuItem startServerMenuItem;
        private ToolStripMenuItem stopServerMenuItem;
        private ToolStripMenuItem settingsMenuItem;
        private ToolStripMenuItem exitMenuItem;
        private ToolStripMenuItem aboutMenuItem;

        private DistributedMasterServer server;
        private TcpListener listener;
        private bool isServerRunning = false;
        private CancellationTokenSource serverCts;
        private Task serverTask;

        // UI Controls
        private GroupBox grpServerStatus;
        private Label lblServerStatus;
        private Label lblConnectedAgents;
        private Label lblActiveBlocks;
        private Label lblFoundResults;
        private Button btnStartServer;
        private Button btnStopServer;
        private TextBox txtServerLog;
        private NumericUpDown numPort;
        private Label lblPort;

        // Server statistics
        private int connectedAgents = 0;
        private int activeBlocks = 0;
        private int foundResults = 0;

        public ServerForm()
        {
            InitializeComponent();
            InitializeMenu();
            server = new DistributedMasterServer();
            UpdateServerStatus();
        }

        private void InitializeComponent()
        {
            this.Text = "Bitcoin Finder - Master Server";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 10F);

            // Server Status Group
            grpServerStatus = new GroupBox();
            grpServerStatus.Text = "Статус сервера";
            grpServerStatus.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            grpServerStatus.Dock = DockStyle.Top;
            grpServerStatus.Height = 120;

            var statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.ColumnCount = 4;
            statusLayout.RowCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            lblServerStatus = new Label();
            lblServerStatus.Text = "Статус: Остановлен";
            lblServerStatus.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblServerStatus.ForeColor = Color.Red;
            lblServerStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblServerStatus.Dock = DockStyle.Fill;
            statusLayout.Controls.Add(lblServerStatus, 0, 0);

            lblConnectedAgents = new Label();
            lblConnectedAgents.Text = "Агентов: 0";
            lblConnectedAgents.Font = new Font("Segoe UI", 11F);
            lblConnectedAgents.TextAlign = ContentAlignment.MiddleCenter;
            lblConnectedAgents.Dock = DockStyle.Fill;
            statusLayout.Controls.Add(lblConnectedAgents, 1, 0);

            lblActiveBlocks = new Label();
            lblActiveBlocks.Text = "Активных блоков: 0";
            lblActiveBlocks.Font = new Font("Segoe UI", 11F);
            lblActiveBlocks.TextAlign = ContentAlignment.MiddleCenter;
            lblActiveBlocks.Dock = DockStyle.Fill;
            statusLayout.Controls.Add(lblActiveBlocks, 2, 0);

            lblFoundResults = new Label();
            lblFoundResults.Text = "Найдено: 0";
            lblFoundResults.Font = new Font("Segoe UI", 11F);
            lblFoundResults.TextAlign = ContentAlignment.MiddleCenter;
            lblFoundResults.Dock = DockStyle.Fill;
            statusLayout.Controls.Add(lblFoundResults, 3, 0);

            // Port configuration
            lblPort = new Label();
            lblPort.Text = "Порт:";
            lblPort.Font = new Font("Segoe UI", 10F);
            lblPort.TextAlign = ContentAlignment.MiddleRight;
            lblPort.Dock = DockStyle.Fill;
            statusLayout.Controls.Add(lblPort, 0, 1);

            numPort = new NumericUpDown();
            numPort.Minimum = 1024;
            numPort.Maximum = 65535;
            numPort.Value = 5000;
            numPort.Font = new Font("Segoe UI", 10F);
            numPort.Dock = DockStyle.Fill;
            statusLayout.Controls.Add(numPort, 1, 1);

            btnStartServer = new Button();
            btnStartServer.Text = "Запустить сервер";
            btnStartServer.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnStartServer.BackColor = Color.LightGreen;
            btnStartServer.Dock = DockStyle.Fill;
            btnStartServer.Click += BtnStartServer_Click;
            statusLayout.Controls.Add(btnStartServer, 2, 1);

            btnStopServer = new Button();
            btnStopServer.Text = "Остановить сервер";
            btnStopServer.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnStopServer.BackColor = Color.LightCoral;
            btnStopServer.Enabled = false;
            btnStopServer.Dock = DockStyle.Fill;
            btnStopServer.Click += BtnStopServer_Click;
            statusLayout.Controls.Add(btnStopServer, 3, 1);

            grpServerStatus.Controls.Add(statusLayout);

            // Server Log
            var grpLog = new GroupBox();
            grpLog.Text = "Лог сервера";
            grpLog.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            grpLog.Dock = DockStyle.Fill;

            txtServerLog = new TextBox();
            txtServerLog.Multiline = true;
            txtServerLog.ScrollBars = ScrollBars.Vertical;
            txtServerLog.ReadOnly = true;
            txtServerLog.Font = new Font("Consolas", 9F);
            txtServerLog.Dock = DockStyle.Fill;
            grpLog.Controls.Add(txtServerLog);

            this.Controls.Add(grpServerStatus);
            this.Controls.Add(grpLog);
        }

        private void InitializeMenu()
        {
            menuStrip = new MenuStrip();
            menuStrip.Font = new Font("Segoe UI", 10F);

            // File Menu
            fileMenu = new ToolStripMenuItem("Файл");
            settingsMenuItem = new ToolStripMenuItem("Настройки", null, SettingsMenuItem_Click);
            exitMenuItem = new ToolStripMenuItem("Выход", null, ExitMenuItem_Click);
            fileMenu.DropDownItems.Add(settingsMenuItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exitMenuItem);

            // Server Menu
            serverMenu = new ToolStripMenuItem("Сервер");
            startServerMenuItem = new ToolStripMenuItem("Запустить сервер", null, StartServerMenuItem_Click);
            stopServerMenuItem = new ToolStripMenuItem("Остановить сервер", null, StopServerMenuItem_Click);
            stopServerMenuItem.Enabled = false;
            serverMenu.DropDownItems.Add(startServerMenuItem);
            serverMenu.DropDownItems.Add(stopServerMenuItem);
            serverMenu.DropDownItems.Add(new ToolStripSeparator());
            serverMenu.DropDownItems.Add(new ToolStripMenuItem("Монитор сервера", null, MonitorMenuItem_Click));

            // Help Menu
            helpMenu = new ToolStripMenuItem("Помощь");
            aboutMenuItem = new ToolStripMenuItem("О программе", null, AboutMenuItem_Click);
            helpMenu.DropDownItems.Add(aboutMenuItem);

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(serverMenu);
            menuStrip.Items.Add(helpMenu);

            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        private void BtnStartServer_Click(object sender, EventArgs e)
        {
            StartServer();
        }

        private void BtnStopServer_Click(object sender, EventArgs e)
        {
            StopServer();
        }

        private void StartServerMenuItem_Click(object sender, EventArgs e)
        {
            StartServer();
        }

        private void StopServerMenuItem_Click(object sender, EventArgs e)
        {
            StopServer();
        }

        private void SettingsMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Настройки сервера будут добавлены в следующей версии.", "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void MonitorMenuItem_Click(object sender, EventArgs e)
        {
            var monitor = new ServerMonitorForm(server);
            monitor.Show();
        }

        private void AboutMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Bitcoin Finder - Master Server\n\n" +
                "Версия: 1.0\n" +
                "Распределенный поиск seed-фраз Bitcoin\n\n" +
                "© 2024 Bitcoin Finder Team",
                "О программе",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            if (isServerRunning)
            {
                var result = MessageBox.Show("Сервер запущен. Остановить сервер и выйти?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    StopServer();
                    this.Close();
                }
            }
            else
            {
                this.Close();
            }
        }

        private void StartServer()
        {
            if (isServerRunning) return;

            try
            {
                int port = (int)numPort.Value;
                listener = new TcpListener(IPAddress.Any, port);
                serverCts = new CancellationTokenSource();
                
                LogMessage($"Запуск сервера на порту {port}...");
                
                serverTask = Task.Run(() => RunServer(serverCts.Token));
                isServerRunning = true;
                
                UpdateServerStatus();
                LogMessage("Сервер успешно запущен");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка запуска сервера: {ex.Message}");
                MessageBox.Show($"Ошибка запуска сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopServer()
        {
            if (!isServerRunning) return;

            try
            {
                LogMessage("Остановка сервера...");
                serverCts?.Cancel();
                listener?.Stop();
                isServerRunning = false;
                UpdateServerStatus();
                LogMessage("Сервер остановлен");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка остановки сервера: {ex.Message}");
            }
        }

        private async Task RunServer(CancellationToken cancellationToken)
        {
            try
            {
                listener.Start();
                LogMessage("Ожидание подключений агентов...");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var client = await listener.AcceptTcpClientAsync();
                        _ = Task.Run(() => HandleClient(client, cancellationToken));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Ошибка принятия подключения: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка работы сервера: {ex.Message}");
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken cancellationToken)
        {
            string clientId = Guid.NewGuid().ToString().Substring(0, 8);
            string clientEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            
            try
            {
                LogMessage($"Агент {clientId} подключился: {clientEndPoint}");
                connectedAgents++;
                UpdateServerStatus();

                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    while (!cancellationToken.IsCancellationRequested && client.Connected)
                    {
                        string? line = await reader.ReadLineAsync();
                        if (line == null) break;

                        var request = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                        if (request == null) continue;

                        string command = request["command"].ToString() ?? "";
                        var response = await ProcessCommand(command, request, clientId);
                        
                        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка обработки агента {clientId}: {ex.Message}");
            }
            finally
            {
                client.Close();
                connectedAgents--;
                UpdateServerStatus();
                LogMessage($"Агент {clientId} отключился");
            }
        }

        private async Task<Dictionary<string, object>> ProcessCommand(string command, Dictionary<string, object> request, string clientId)
        {
            switch (command)
            {
                case "GET_TASK":
                    return await GetTask(clientId);
                case "REPORT_PROGRESS":
                    return await ReportProgress(request, clientId);
                case "REPORT_FOUND":
                    return await ReportFound(request, clientId);
                case "RELEASE_BLOCK":
                    return await ReleaseBlock(request, clientId);
                default:
                    return new Dictionary<string, object> { { "error", "Unknown command" } };
            }
        }

        private async Task<Dictionary<string, object>> GetTask(string clientId)
        {
            var block = server.GetNextBlock(clientId);
            if (block == null)
                return new Dictionary<string, object> { { "command", "NO_TASK" } };
            return new Dictionary<string, object> {
                { "command", "TASK" },
                { "blockId", block.BlockId },
                { "startIndex", block.StartIndex },
                { "endIndex", block.EndIndex },
                { "wordCount", server.WordCount },
                { "address", server.BitcoinAddress },
                { "fullSearch", server.FullSearch }
            };
        }

        private async Task<Dictionary<string, object>> ReportProgress(Dictionary<string, object> request, string clientId)
        {
            if (request.TryGetValue("blockId", out var blockIdObj) &&
                request.TryGetValue("currentIndex", out var currentIndexObj))
            {
                int blockId = Convert.ToInt32(blockIdObj);
                long currentIndex = Convert.ToInt64(currentIndexObj);
                server.ReportProgress(blockId, clientId, currentIndex);
            }
            return new Dictionary<string, object> { { "status", "ok" } };
        }

        private async Task<Dictionary<string, object>> ReportFound(Dictionary<string, object> request, string clientId)
        {
            if (request.TryGetValue("combination", out var phraseObj))
            {
                string phrase = phraseObj?.ToString() ?? "";
                server.ReportFound(phrase);
            }
            return new Dictionary<string, object> { { "status", "ok" } };
        }

        private async Task<Dictionary<string, object>> ReleaseBlock(Dictionary<string, object> request, string clientId)
        {
            if (request.TryGetValue("blockId", out var blockIdObj2))
            {
                int blockId = Convert.ToInt32(blockIdObj2);
                server.ReleaseBlock(blockId, clientId);
            }
            return new Dictionary<string, object> { { "status", "ok" } };
        }

        private void UpdateServerStatus()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateServerStatus));
                return;
            }

            if (isServerRunning)
            {
                lblServerStatus.Text = "Статус: Запущен";
                lblServerStatus.ForeColor = Color.Green;
                btnStartServer.Enabled = false;
                btnStopServer.Enabled = true;
                startServerMenuItem.Enabled = false;
                stopServerMenuItem.Enabled = true;
            }
            else
            {
                lblServerStatus.Text = "Статус: Остановлен";
                lblServerStatus.ForeColor = Color.Red;
                btnStartServer.Enabled = true;
                btnStopServer.Enabled = false;
                startServerMenuItem.Enabled = true;
                stopServerMenuItem.Enabled = false;
            }

            activeBlocks = server.BlockQueue.Count(b => b.Status == "assigned");
            foundResults = server.FoundResults.Count;
            lblActiveBlocks.Text = $"Активных блоков: {activeBlocks}";
            lblFoundResults.Text = $"Найдено: {foundResults}";
        }

        private void LogMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(LogMessage), message);
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtServerLog.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
            txtServerLog.SelectionStart = txtServerLog.Text.Length;
            txtServerLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isServerRunning)
            {
                var result = MessageBox.Show("Сервер запущен. Остановить сервер и выйти?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    StopServer();
                }
                else
                {
                    e.Cancel = true;
                }
            }
            base.OnFormClosing(e);
        }
    }
} 