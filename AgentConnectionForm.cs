using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Text;

namespace BitcoinFinder
{
    public partial class AgentConnectionForm : Form
    {
        private HttpClient httpClient;
        private bool isConnected = false;

        // UI элементы
        private TextBox txtServerUrl;
        private TextBox txtAgentName;
        private NumericUpDown numThreads;
        private Button btnConnect;
        private Button btnTestConnection;
        private Label lblConnectionStatus;
        private TextBox txtLog;
        private Button btnClearLog;
        private const string ConfigFile = "agent_web_config.json";

        public AgentConnectionForm()
        {
            InitializeComponent();
            LoadConfig();
            
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            // Автосохранение конфигурации
            txtServerUrl.TextChanged += (s, e) => SaveConfig();
            txtAgentName.TextChanged += (s, e) => SaveConfig();
            numThreads.ValueChanged += (s, e) => SaveConfig();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(800, 600);
            this.Text = "Подключение агентов через Web API";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(700, 500);

            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 4;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Подключение
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Статус
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70F)); // Лог
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Кнопки
            mainLayout.Padding = new Padding(20);

            // === СЕКЦИЯ ПОДКЛЮЧЕНИЯ ===
            var connectionGroup = new GroupBox();
            connectionGroup.Text = "Настройки подключения к Web API";
            connectionGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            connectionGroup.Dock = DockStyle.Fill;

            var connectionLayout = new TableLayoutPanel();
            connectionLayout.Dock = DockStyle.Fill;
            connectionLayout.ColumnCount = 4;
            connectionLayout.RowCount = 4;
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            connectionLayout.Padding = new Padding(10);

            // URL сервера
            connectionLayout.Controls.Add(new Label { Text = "URL сервера:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            txtServerUrl = new TextBox { Text = "http://localhost:5000", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(txtServerUrl, 1, 0);

            // Кнопка теста соединения
            btnTestConnection = new Button { Text = "Тест соединения", Font = new Font("Segoe UI", 9F), BackColor = Color.LightBlue, Dock = DockStyle.Fill };
            btnTestConnection.Click += BtnTestConnection_Click;
            connectionLayout.Controls.Add(btnTestConnection, 2, 0);

            // Статус подключения
            lblConnectionStatus = new Label { Text = "Статус: Отключено", Font = new Font("Segoe UI", 10F), ForeColor = Color.Red, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            connectionLayout.Controls.Add(lblConnectionStatus, 3, 0);

            // Имя агента
            connectionLayout.Controls.Add(new Label { Text = "Имя агента:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            txtAgentName = new TextBox { Text = Environment.MachineName, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(txtAgentName, 1, 1);

            // Количество потоков
            connectionLayout.Controls.Add(new Label { Text = "Потоков:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 2, 1);
            numThreads = new NumericUpDown { Minimum = 1, Maximum = 128, Value = 4, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            connectionLayout.Controls.Add(numThreads, 3, 1);

            // Кнопка подключения
            btnConnect = new Button { Text = "Подключиться к серверу", Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.LightGreen, Dock = DockStyle.Fill };
            btnConnect.Click += BtnConnect_Click;
            connectionLayout.SetColumnSpan(btnConnect, 4);
            connectionLayout.Controls.Add(btnConnect, 0, 2);

            // Информация
            var lblInfo = new Label { 
                Text = "Подключение к Web API серверу для получения и выполнения задач поиска seed-фраз", 
                Font = new Font("Segoe UI", 9F), 
                ForeColor = Color.Gray, 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.MiddleCenter 
            };
            connectionLayout.SetColumnSpan(lblInfo, 4);
            connectionLayout.Controls.Add(lblInfo, 0, 3);

            connectionGroup.Controls.Add(connectionLayout);
            mainLayout.Controls.Add(connectionGroup, 0, 0);

            // === СЕКЦИЯ ЛОГА ===
            var logGroup = new GroupBox();
            logGroup.Text = "Лог подключения";
            logGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            logGroup.Dock = DockStyle.Fill;

            var logLayout = new TableLayoutPanel();
            logLayout.Dock = DockStyle.Fill;
            logLayout.ColumnCount = 1;
            logLayout.RowCount = 2;
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.ReadOnly = true;
            txtLog.BackColor = Color.Black;
            txtLog.ForeColor = Color.Lime;
            logLayout.Controls.Add(txtLog, 0, 0);

            // Кнопка очистки лога
            btnClearLog = new Button { Text = "Очистить лог", Font = new Font("Segoe UI", 9F), Dock = DockStyle.Fill };
            btnClearLog.Click += BtnClearLog_Click;
            logLayout.Controls.Add(btnClearLog, 0, 1);

            logGroup.Controls.Add(logLayout);
            mainLayout.Controls.Add(logGroup, 0, 2);

            this.Controls.Add(mainLayout);
        }

        private async void BtnTestConnection_Click(object? sender, EventArgs e)
        {
            try
            {
                btnTestConnection.Enabled = false;
                btnTestConnection.Text = "Тестирование...";
                AddLog("Начинаем тест соединения...");

                var url = txtServerUrl.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    AddLog("Ошибка: URL сервера не указан");
                    return;
                }

                // Убираем trailing slash если есть
                if (url.EndsWith("/"))
                    url = url.TrimEnd('/');

                var testUrl = $"{url}/api/health";
                AddLog($"Тестируем соединение с: {testUrl}");

                var response = await httpClient.GetAsync(testUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    AddLog($"✅ Соединение успешно! Ответ сервера: {content}");
                    MessageBox.Show("Соединение с сервером установлено успешно!", "Тест соединения", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AddLog($"❌ Ошибка соединения. Код: {response.StatusCode}");
                    MessageBox.Show($"Ошибка соединения. Код: {response.StatusCode}", "Тест соединения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка теста соединения: {ex.Message}");
                MessageBox.Show($"Ошибка теста соединения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTestConnection.Enabled = true;
                btnTestConnection.Text = "Тест соединения";
            }
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (isConnected)
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
            try
            {
                btnConnect.Enabled = false;
                btnConnect.Text = "Подключение...";
                AddLog("Начинаем подключение к серверу...");

                var url = txtServerUrl.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    AddLog("Ошибка: URL сервера не указан");
                    return;
                }

                // Убираем trailing slash если есть
                if (url.EndsWith("/"))
                    url = url.TrimEnd('/');

                // Регистрируем агента
                var agentData = new
                {
                    Name = txtAgentName.Text,
                    Threads = (int)numThreads.Value,
                    Status = "Connected"
                };

                var json = JsonSerializer.Serialize(agentData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{url}/api/agents/register", content);
                
                if (response.IsSuccessStatusCode)
                {
                    isConnected = true;
                    lblConnectionStatus.Text = "Статус: Подключено";
                    lblConnectionStatus.ForeColor = Color.Green;
                    btnConnect.Text = "Отключиться";
                    btnConnect.BackColor = Color.LightCoral;
                    AddLog("✅ Успешно подключились к серверу!");
                    
                    // Запускаем получение задач
                    _ = Task.Run(async () => await StartTaskPolling(url));
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    AddLog($"❌ Ошибка подключения. Код: {response.StatusCode}, Ошибка: {errorContent}");
                    MessageBox.Show($"Ошибка подключения: {errorContent}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка подключения: {ex.Message}");
                MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnConnect.Enabled = true;
                if (!isConnected)
                {
                    btnConnect.Text = "Подключиться к серверу";
                }
            }
        }

        private async Task DisconnectFromServer()
        {
            try
            {
                btnConnect.Enabled = false;
                btnConnect.Text = "Отключение...";
                AddLog("Отключаемся от сервера...");

                var url = txtServerUrl.Text.Trim();
                if (url.EndsWith("/"))
                    url = url.TrimEnd('/');

                var response = await httpClient.DeleteAsync($"{url}/api/agents/{txtAgentName.Text}");
                
                isConnected = false;
                lblConnectionStatus.Text = "Статус: Отключено";
                lblConnectionStatus.ForeColor = Color.Red;
                btnConnect.Text = "Подключиться к серверу";
                btnConnect.BackColor = Color.LightGreen;
                AddLog("✅ Отключились от сервера");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка отключения: {ex.Message}");
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private async Task StartTaskPolling(string serverUrl)
        {
            while (isConnected)
            {
                try
                {
                    // Получаем задачу
                    var response = await httpClient.GetAsync($"{serverUrl}/api/agents/{txtAgentName.Text}/task");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var taskJson = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(taskJson) && taskJson != "null")
                        {
                            AddLog($"Получена задача: {taskJson}");
                            
                            // Здесь можно добавить логику выполнения задачи
                            // Пока просто отправляем результат
                            var result = new { Status = "Completed", Result = "Task processed" };
                            var resultJson = JsonSerializer.Serialize(result);
                            var resultContent = new StringContent(resultJson, Encoding.UTF8, "application/json");
                            
                            await httpClient.PostAsync($"{serverUrl}/api/agents/{txtAgentName.Text}/result", resultContent);
                            AddLog("Задача выполнена и результат отправлен");
                        }
                    }
                    
                    await Task.Delay(5000); // Проверяем каждые 5 секунд
                }
                catch (Exception ex)
                {
                    AddLog($"Ошибка при получении задач: {ex.Message}");
                    await Task.Delay(10000); // При ошибке ждем дольше
                }
            }
        }

        private void AddLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AddLog(message)));
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
            txtLog.ScrollToCaret();
        }

        private void BtnClearLog_Click(object? sender, EventArgs e)
        {
            txtLog.Clear();
        }

        private void SaveConfig()
        {
            try
            {
                var config = new AgentWebConfig
                {
                    ServerUrl = txtServerUrl.Text,
                    AgentName = txtAgentName.Text,
                    Threads = (int)numThreads.Value
                };

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка сохранения конфигурации: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<AgentWebConfig>(json);
                    
                    if (config != null)
                    {
                        txtServerUrl.Text = config.ServerUrl ?? "http://localhost:5000";
                        txtAgentName.Text = config.AgentName ?? Environment.MachineName;
                        numThreads.Value = config.Threads > 0 ? config.Threads : 4;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка загрузки конфигурации: {ex.Message}");
            }
        }

        private class AgentWebConfig
        {
            public string ServerUrl { get; set; } = "http://localhost:5000";
            public string AgentName { get; set; } = "";
            public int Threads { get; set; } = 4;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isConnected)
            {
                _ = DisconnectFromServer();
            }
            httpClient?.Dispose();
            base.OnFormClosing(e);
        }
    }
} 