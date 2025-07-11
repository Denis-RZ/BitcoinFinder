using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NBitcoin;
using System.Text.Json;
using System.IO;
using System.Collections.Generic;

namespace BitcoinFinder
{
    public partial class StandaloneForm : Form
    {
        private AdvancedSeedPhraseFinder? finder;
        private BackgroundWorker? searchWorker;
        private System.Windows.Forms.Timer? autoSaveTimer;
        
        // UI элементы
        private TextBox txtSeedPhrase = null!;
        private TextBox txtBitcoinAddress = null!;
        private NumericUpDown numWordCount = null!;
        private NumericUpDown numThreadCount = null!;
        private CheckBox chkFullSearch = null!;
        private Button btnStartSearch = null!;
        private Button btnStopSearch = null!;
        private Button btnSaveProgress = null!;
        private Button btnLoadProgress = null!;
        private ProgressBar progressBar = null!;
        private Label lblProgress = null!;
        private Label lblSpeed = null!;
        private Label lblTimeRemaining = null!;
        private Label lblValidationStatus = null!;
        private TextBox txtResults = null!;
        private TextBox txtLog = null!;

        public StandaloneForm()
        {
            InitializeFormElements();
            InitializeComponent();
            LoadFormConfig();
            SetupAutoSaveTimer();
            SetupValidation();
            
            this.FormClosing += StandaloneForm_FormClosing;
        }

        private void InitializeFormElements()
        {
            txtSeedPhrase = new TextBox();
            txtBitcoinAddress = new TextBox();
            numWordCount = new NumericUpDown();
            numThreadCount = new NumericUpDown();
            chkFullSearch = new CheckBox();
            btnStartSearch = new Button();
            btnStopSearch = new Button();
            btnSaveProgress = new Button();
            btnLoadProgress = new Button();
            progressBar = new ProgressBar();
            lblProgress = new Label();
            lblSpeed = new Label();
            lblTimeRemaining = new Label();
            lblValidationStatus = new Label();
            txtResults = new TextBox();
            txtLog = new TextBox();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(1200, 800);
            this.Text = "Bitcoin Finder - Standalone Mode";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(1000, 600);

            // === ОСНОВНАЯ РАЗМЕТКА ===
            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 3;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F)); // Ввод и настройки
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F)); // Прогресс и результаты
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F)); // Логи

            // === ГРУППА ПАРАМЕТРОВ ПОИСКА ===
            var inputGroup = new GroupBox();
            inputGroup.Text = "🔍 Параметры поиска";
            inputGroup.Dock = DockStyle.Fill;
            inputGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            var inputLayout = new TableLayoutPanel();
            inputLayout.Dock = DockStyle.Fill;
            inputLayout.ColumnCount = 2;
            inputLayout.RowCount = 8;
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inputLayout.Padding = new Padding(10);

            // Seed фраза
            inputLayout.Controls.Add(new Label { 
                Text = "Seed фраза:", 
                Font = new Font("Segoe UI", 10F), 
                TextAlign = ContentAlignment.MiddleRight 
            }, 0, 0);
            txtSeedPhrase = new TextBox { 
                Text = "* * * * * * * * * * * *", 
                Font = new Font("Consolas", 10F), 
                Dock = DockStyle.Fill,
                Multiline = true,
                Height = 60
            };
            txtSeedPhrase.TextChanged += TxtSeedPhrase_TextChanged;
            inputLayout.Controls.Add(txtSeedPhrase, 1, 0);

            // Bitcoin адрес
            inputLayout.Controls.Add(new Label { 
                Text = "Bitcoin адрес:", 
                Font = new Font("Segoe UI", 10F), 
                TextAlign = ContentAlignment.MiddleRight 
            }, 0, 1);
            txtBitcoinAddress = new TextBox { 
                Text = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ", 
                Font = new Font("Consolas", 10F), 
                Dock = DockStyle.Fill 
            };
            txtBitcoinAddress.TextChanged += TxtBitcoinAddress_TextChanged;
            inputLayout.Controls.Add(txtBitcoinAddress, 1, 1);

            // Статус валидации
            inputLayout.Controls.Add(new Label { 
                Text = "Статус:", 
                Font = new Font("Segoe UI", 10F), 
                TextAlign = ContentAlignment.MiddleRight 
            }, 0, 2);
            lblValidationStatus = new Label { 
                Text = "✅ Готов к поиску", 
                Font = new Font("Segoe UI", 10F, FontStyle.Bold), 
                ForeColor = Color.Green,
                Dock = DockStyle.Fill 
            };
            inputLayout.Controls.Add(lblValidationStatus, 1, 2);

            // Количество слов
            inputLayout.Controls.Add(new Label { 
                Text = "Количество слов:", 
                Font = new Font("Segoe UI", 10F), 
                TextAlign = ContentAlignment.MiddleRight 
            }, 0, 3);
            numWordCount = new NumericUpDown { 
                Value = 12, 
                Minimum = 12, 
                Maximum = 24, 
                Increment = 6, 
                Font = new Font("Segoe UI", 10F), 
                Dock = DockStyle.Fill 
            };
            numWordCount.ValueChanged += NumWordCount_ValueChanged;
            inputLayout.Controls.Add(numWordCount, 1, 3);

            // Количество потоков
            inputLayout.Controls.Add(new Label { 
                Text = "Потоков:", 
                Font = new Font("Segoe UI", 10F), 
                TextAlign = ContentAlignment.MiddleRight 
            }, 0, 4);
            numThreadCount = new NumericUpDown { 
                Value = Math.Min(4, Environment.ProcessorCount), 
                Minimum = 1, 
                Maximum = Environment.ProcessorCount * 2, 
                Font = new Font("Segoe UI", 10F), 
                Dock = DockStyle.Fill 
            };
            inputLayout.Controls.Add(numThreadCount, 1, 4);

            // Полный перебор
            chkFullSearch = new CheckBox { 
                Text = "⚠️ Полный перебор (крайне опасно!)", 
                Font = new Font("Segoe UI", 10F, FontStyle.Bold), 
                ForeColor = Color.Red,
                Dock = DockStyle.Fill 
            };
            chkFullSearch.CheckedChanged += ChkFullSearch_CheckedChanged;
            inputLayout.Controls.Add(chkFullSearch, 1, 5);
            inputLayout.SetColumnSpan(chkFullSearch, 2);

            // Пустая строка для разделения
            inputLayout.Controls.Add(new Label(), 0, 6);

            // Кнопки управления
            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.LeftToRight;
            buttonPanel.WrapContents = true;

            btnStartSearch = new Button { 
                Text = "▶️ Начать поиск", 
                Font = new Font("Segoe UI", 11F, FontStyle.Bold), 
                BackColor = Color.LightGreen,
                ForeColor = Color.DarkGreen,
                Size = new Size(140, 40),
                Margin = new Padding(5)
            };
            btnStartSearch.Click += BtnStartSearch_Click;
            buttonPanel.Controls.Add(btnStartSearch);

            btnStopSearch = new Button { 
                Text = "⏹️ Остановить", 
                Font = new Font("Segoe UI", 11F, FontStyle.Bold), 
                BackColor = Color.LightCoral,
                ForeColor = Color.DarkRed,
                Enabled = false,
                Size = new Size(140, 40),
                Margin = new Padding(5)
            };
            btnStopSearch.Click += BtnStopSearch_Click;
            buttonPanel.Controls.Add(btnStopSearch);

            btnSaveProgress = new Button { 
                Text = "💾 Сохранить", 
                Font = new Font("Segoe UI", 10F), 
                Size = new Size(110, 35),
                Margin = new Padding(5)
            };
            btnSaveProgress.Click += BtnSaveProgress_Click;
            buttonPanel.Controls.Add(btnSaveProgress);

            btnLoadProgress = new Button { 
                Text = "📂 Загрузить", 
                Font = new Font("Segoe UI", 10F), 
                Size = new Size(110, 35),
                Margin = new Padding(5)
            };
            btnLoadProgress.Click += BtnLoadProgress_Click;
            buttonPanel.Controls.Add(btnLoadProgress);

            inputLayout.Controls.Add(buttonPanel, 0, 7);
            inputLayout.SetColumnSpan(buttonPanel, 2);

            inputGroup.Controls.Add(inputLayout);
            mainLayout.Controls.Add(inputGroup, 0, 0);

            // === ГРУППА ПРОГРЕССА ===
            var progressGroup = new GroupBox();
            progressGroup.Text = "📊 Прогресс поиска";
            progressGroup.Dock = DockStyle.Fill;
            progressGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            var progressLayout = new TableLayoutPanel();
            progressLayout.Dock = DockStyle.Fill;
            progressLayout.RowCount = 5;
            progressLayout.ColumnCount = 1;
            progressLayout.Padding = new Padding(10);

            // Прогресс бар
            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Height = 30;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressLayout.Controls.Add(progressBar, 0, 0);

            // Процент выполнения
            lblProgress = new Label();
            lblProgress.Text = "0% (0 / 0)";
            lblProgress.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblProgress.TextAlign = ContentAlignment.MiddleCenter;
            lblProgress.Dock = DockStyle.Fill;
            lblProgress.ForeColor = Color.DarkBlue;
            progressLayout.Controls.Add(lblProgress, 0, 1);

            // Скорость
            lblSpeed = new Label();
            lblSpeed.Text = "Скорость: 0 комб/сек";
            lblSpeed.Font = new Font("Segoe UI", 12F);
            lblSpeed.TextAlign = ContentAlignment.MiddleCenter;
            lblSpeed.Dock = DockStyle.Fill;
            lblSpeed.ForeColor = Color.DarkGreen;
            progressLayout.Controls.Add(lblSpeed, 0, 2);

            // Оставшееся время
            lblTimeRemaining = new Label();
            lblTimeRemaining.Text = "Оставшееся время: неизвестно";
            lblTimeRemaining.Font = new Font("Segoe UI", 12F);
            lblTimeRemaining.TextAlign = ContentAlignment.MiddleCenter;
            lblTimeRemaining.Dock = DockStyle.Fill;
            lblTimeRemaining.ForeColor = Color.DarkOrange;
            progressLayout.Controls.Add(lblTimeRemaining, 0, 3);

            // Инструкция
            var instructionLabel = new Label();
            instructionLabel.Text = "💡 Совет: используйте * для неизвестных слов\nПример: abandon * * * * * * * * * * *";
            instructionLabel.Font = new Font("Segoe UI", 9F);
            instructionLabel.TextAlign = ContentAlignment.MiddleCenter;
            instructionLabel.Dock = DockStyle.Fill;
            instructionLabel.ForeColor = Color.Gray;
            progressLayout.Controls.Add(instructionLabel, 0, 4);

            progressGroup.Controls.Add(progressLayout);
            mainLayout.Controls.Add(progressGroup, 1, 0);

            // === РЕЗУЛЬТАТЫ ===
            var resultsGroup = new GroupBox();
            resultsGroup.Text = "🎯 Результаты поиска";
            resultsGroup.Dock = DockStyle.Fill;
            resultsGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            txtResults = new TextBox();
            txtResults.Dock = DockStyle.Fill;
            txtResults.Multiline = true;
            txtResults.ScrollBars = ScrollBars.Both;
            txtResults.Font = new Font("Consolas", 10F);
            txtResults.BackColor = Color.Black;
            txtResults.ForeColor = Color.Yellow;
            txtResults.ReadOnly = true;
            txtResults.Text = "Здесь будут отображаться найденные seed-фразы...\r\n";
            resultsGroup.Controls.Add(txtResults);
            mainLayout.Controls.Add(resultsGroup, 0, 1);
            mainLayout.SetColumnSpan(resultsGroup, 2);

            // === ЛОГ ===
            var logGroup = new GroupBox();
            logGroup.Text = "📋 Журнал событий";
            logGroup.Dock = DockStyle.Fill;
            logGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.ReadOnly = true;
            txtLog.BackColor = Color.WhiteSmoke;
            logGroup.Controls.Add(txtLog);
            mainLayout.Controls.Add(logGroup, 0, 2);
            mainLayout.SetColumnSpan(logGroup, 2);

            this.Controls.Add(mainLayout);

            // Добавляем приветственное сообщение
            AddLog("🚀 Bitcoin Finder (Standalone) запущен");
            AddLog("💡 Введите seed-фразу с * для неизвестных слов");
            AddLog("⚠️ Внимание: полный перебор может занять очень много времени!");
        }

        private void SetupAutoSaveTimer()
        {
            autoSaveTimer = new System.Windows.Forms.Timer();
            autoSaveTimer.Interval = 60000; // 1 минута
            autoSaveTimer.Tick += AutoSaveTimer_Tick;
        }

        private void SetupValidation()
        {
            ValidateInputs();
        }

        #region Валидация

        private void TxtSeedPhrase_TextChanged(object? sender, EventArgs e)
        {
            ValidateInputs();
        }

        private void TxtBitcoinAddress_TextChanged(object? sender, EventArgs e)
        {
            ValidateInputs();
        }

        private void NumWordCount_ValueChanged(object? sender, EventArgs e)
        {
            // Обновляем seed-фразу при изменении количества слов
            var currentWords = txtSeedPhrase.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var targetCount = (int)numWordCount.Value;
            
            if (currentWords.Length != targetCount)
            {
                var newWords = new string[targetCount];
                for (int i = 0; i < targetCount; i++)
                {
                    newWords[i] = i < currentWords.Length ? currentWords[i] : "*";
                }
                txtSeedPhrase.Text = string.Join(" ", newWords);
            }
            ValidateInputs();
        }

        private void ChkFullSearch_CheckedChanged(object? sender, EventArgs e)
        {
            if (chkFullSearch.Checked)
            {
                var result = MessageBox.Show(
                    "⚠️ КРАЙНЕ ВАЖНОЕ ПРЕДУПРЕЖДЕНИЕ! ⚠️\n\n" +
                    "Полный перебор для 12 слов = 2048^12 ≈ 5.4×10³⁹ комбинаций\n" +
                    "При скорости 1 миллион комб/сек это займет ~1.7×10²⁶ лет!\n\n" +
                    "Это больше чем возраст Вселенной в 10¹⁶ раз!\n\n" +
                    "Рекомендуется использовать частично известные слова.\n\n" +
                    "ВЫ ДЕЙСТВИТЕЛЬНО ХОТИТЕ ПРОДОЛЖИТЬ?",
                    "🚨 КРИТИЧЕСКОЕ ПРЕДУПРЕЖДЕНИЕ",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (result == DialogResult.No)
                {
                    chkFullSearch.Checked = false;
                }
                else
                {
                    AddLog("⚠️ ВКЛЮЧЕН ПОЛНЫЙ ПЕРЕБОР! Пользователь предупрежден о рисках.");
                }
            }
            ValidateInputs();
        }

        private void ValidateInputs()
        {
            var issues = new List<string>();

            // Проверка seed-фразы
            if (!string.IsNullOrWhiteSpace(txtSeedPhrase.Text))
            {
                var words = txtSeedPhrase.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length != numWordCount.Value)
                {
                    issues.Add($"Нужно {numWordCount.Value} слов, получено {words.Length}");
                }

                // Проверка BIP39 слов
                var wordlist = Wordlist.English.GetWords().ToList();
                foreach (var word in words)
                {
                    if (word != "*" && !word.Contains("*") && !wordlist.Contains(word))
                    {
                        issues.Add($"'{word}' не из BIP39 словаря");
                        break;
                    }
                }

                // Проверка разумности количества неизвестных слов
                var unknownCount = words.Count(w => w == "*" || w.Contains("*"));
                if (unknownCount > 6 && !chkFullSearch.Checked)
                {
                    issues.Add($"Слишком много неизвестных слов ({unknownCount}). Рекомендуется ≤4");
                }
            }

            // Проверка Bitcoin адреса
            if (!string.IsNullOrWhiteSpace(txtBitcoinAddress.Text))
            {
                if (!IsValidBitcoinAddress(txtBitcoinAddress.Text))
                {
                    issues.Add("Неверный формат Bitcoin адреса");
                }
            }

            // Обновляем статус
            if (issues.Count == 0)
            {
                lblValidationStatus.Text = "✅ Готов к поиску";
                lblValidationStatus.ForeColor = Color.Green;
                btnStartSearch.Enabled = true;
            }
            else
            {
                lblValidationStatus.Text = $"❌ {string.Join("; ", issues)}";
                lblValidationStatus.ForeColor = Color.Red;
                btnStartSearch.Enabled = false;
            }
        }

        private bool IsValidBitcoinAddress(string address)
        {
            try
            {
                // Проверяем различные типы Bitcoin адресов
                BitcoinAddress.Create(address, Network.Main);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Поиск

        private void BtnStartSearch_Click(object? sender, EventArgs e)
        {
            StartSearch();
        }

        private void StartSearch()
        {
            if (searchWorker != null && searchWorker.IsBusy)
            {
                AddLog("❌ Поиск уже выполняется");
                return;
            }

            try
            {
                // Создаем параметры поиска
                var parameters = new SearchParameters
                {
                    SeedPhrase = txtSeedPhrase.Text.Trim(),
                    BitcoinAddress = txtBitcoinAddress.Text.Trim(),
                    WordCount = (int)numWordCount.Value,
                    ThreadCount = (int)numThreadCount.Value,
                    FullSearch = chkFullSearch.Checked
                };

                // Логируем начало поиска
                AddLog($"🚀 Запуск поиска...");
                AddLog($"📝 Seed-фраза: {parameters.SeedPhrase}");
                AddLog($"🎯 Bitcoin адрес: {parameters.BitcoinAddress}");
                AddLog($"🔢 Количество слов: {parameters.WordCount}");
                AddLog($"⚡ Потоков: {parameters.ThreadCount}");
                AddLog($"🔍 Полный перебор: {(parameters.FullSearch ? "ДА" : "НЕТ")}");

                // Инициализируем поисковик
                finder = new AdvancedSeedPhraseFinder();

                // Настраиваем BackgroundWorker
                searchWorker = new BackgroundWorker();
                searchWorker.WorkerReportsProgress = true;
                searchWorker.WorkerSupportsCancellation = true;
                searchWorker.DoWork += SearchWorker_DoWork;
                searchWorker.ProgressChanged += SearchWorker_ProgressChanged;
                searchWorker.RunWorkerCompleted += SearchWorker_RunWorkerCompleted;

                // Запускаем поиск
                searchWorker.RunWorkerAsync(parameters);

                // Обновляем UI
                btnStartSearch.Enabled = false;
                btnStopSearch.Enabled = true;
                autoSaveTimer?.Start();

                txtResults.Text = $"🔍 Поиск запущен в {DateTime.Now:HH:mm:ss}\r\n";
                txtResults.AppendText("Ожидание результатов...\r\n");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка запуска поиска: {ex.Message}");
                MessageBox.Show($"Ошибка запуска поиска: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStopSearch_Click(object? sender, EventArgs e)
        {
            StopSearch();
        }

        private void StopSearch()
        {
            if (searchWorker != null && searchWorker.IsBusy)
            {
                searchWorker.CancelAsync();
                finder?.Cancel();
                AddLog("⏹️ Запрос остановки поиска отправлен...");
            }
        }

        private void SearchWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            var parameters = (SearchParameters)e.Argument!;
            finder?.Search(parameters, (BackgroundWorker)sender!, e);
        }

        private void SearchWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            try
            {
                if (e.UserState is string message)
                {
                    if (message.StartsWith("FOUND:") || message.StartsWith("НАЙДЕНО"))
                    {
                        // Найден результат!
                        txtResults.AppendText($"🎉 {DateTime.Now:HH:mm:ss} - {message}\r\n");
                        txtResults.SelectionStart = txtResults.Text.Length;
                        txtResults.ScrollToCaret();
                        AddLog($"🎉 {message}");
                        
                        // Показываем уведомление
                        this.WindowState = FormWindowState.Normal;
                        this.BringToFront();
                        MessageBox.Show(message, "🎉 НАЙДЕНО!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (message.Contains("%"))
                    {
                        // Обновляем прогресс
                        var parts = message.Split('(', ')');
                        if (parts.Length >= 2)
                        {
                            var progressPart = parts[0].Trim().Replace("%", "");
                            if (double.TryParse(progressPart, out double percent))
                            {
                                progressBar.Value = Math.Max(0, Math.Min(100, (int)percent));
                            }
                            lblProgress.Text = parts.Length > 1 ? $"{parts[0]} ({parts[1]})" : message;
                        }
                    }
                    else if (message.Contains("скорость") || message.ToLower().Contains("speed"))
                    {
                        lblSpeed.Text = message;
                    }
                    else if (message.Contains("время") || message.ToLower().Contains("time"))
                    {
                        lblTimeRemaining.Text = message;
                    }
                    else
                    {
                        AddLog(message);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка обновления прогресса: {ex.Message}");
            }
        }

        private void SearchWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                btnStartSearch.Enabled = true;
                btnStopSearch.Enabled = false;
                autoSaveTimer?.Stop();

                if (e.Cancelled)
                {
                    AddLog("⏹️ Поиск отменен пользователем");
                    txtResults.AppendText($"⏹️ {DateTime.Now:HH:mm:ss} - Поиск отменен\r\n");
                    lblProgress.Text = "Поиск отменен";
                }
                else if (e.Error != null)
                {
                    AddLog($"❌ Ошибка поиска: {e.Error.Message}");
                    txtResults.AppendText($"❌ {DateTime.Now:HH:mm:ss} - Ошибка: {e.Error.Message}\r\n");
                    lblProgress.Text = "Ошибка поиска";
                }
                else
                {
                    AddLog("✅ Поиск завершен успешно");
                    txtResults.AppendText($"✅ {DateTime.Now:HH:mm:ss} - Поиск завершен\r\n");
                    lblProgress.Text = "Поиск завершен";
                }

                progressBar.Value = 0;
                lblSpeed.Text = "Скорость: 0 комб/сек";
                lblTimeRemaining.Text = "Оставшееся время: неизвестно";
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка завершения поиска: {ex.Message}");
            }
        }

        #endregion

        #region Сохранение/загрузка

        private void BtnSaveProgress_Click(object? sender, EventArgs e)
        {
            try
            {
                using var dialog = new SaveFileDialog();
                dialog.Filter = "JSON files (*.json)|*.json";
                dialog.DefaultExt = "json";
                dialog.FileName = $"standalone_progress_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var progressData = new ProgressData
                    {
                        SeedPhrase = txtSeedPhrase.Text,
                        BitcoinAddress = txtBitcoinAddress.Text,
                        WordCount = (int)numWordCount.Value,
                        FullSearch = chkFullSearch.Checked,
                        ThreadCount = (int)numThreadCount.Value,
                        Timestamp = DateTime.Now
                    };
                    
                    var json = JsonSerializer.Serialize(progressData, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    
                    AddLog($"💾 Настройки сохранены: {Path.GetFileName(dialog.FileName)}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка сохранения: {ex.Message}");
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnLoadProgress_Click(object? sender, EventArgs e)
        {
            try
            {
                using var dialog = new OpenFileDialog();
                dialog.Filter = "JSON files (*.json)|*.json";
                dialog.DefaultExt = "json";
                
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var progressData = JsonSerializer.Deserialize<ProgressData>(json);
                    
                    if (progressData != null)
                    {
                        txtSeedPhrase.Text = progressData.SeedPhrase;
                        txtBitcoinAddress.Text = progressData.BitcoinAddress;
                        numWordCount.Value = progressData.WordCount;
                        chkFullSearch.Checked = progressData.FullSearch;
                        numThreadCount.Value = progressData.ThreadCount;
                        
                        AddLog($"📂 Настройки загружены: {Path.GetFileName(dialog.FileName)}");
                        ValidateInputs();
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка загрузки: {ex.Message}");
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (searchWorker != null && searchWorker.IsBusy)
                {
                    var autoSavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standalone_auto_save.json");
                    var progressData = new ProgressData
                    {
                        SeedPhrase = txtSeedPhrase.Text,
                        BitcoinAddress = txtBitcoinAddress.Text,
                        WordCount = (int)numWordCount.Value,
                        FullSearch = chkFullSearch.Checked,
                        ThreadCount = (int)numThreadCount.Value,
                        Timestamp = DateTime.Now
                    };
                    
                    var json = JsonSerializer.Serialize(progressData, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(autoSavePath, json);
                    
                    AddLog("💾 Автосохранение выполнено");
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка автосохранения: {ex.Message}");
            }
        }

        #endregion

        #region Конфигурация

        private void LoadFormConfig()
        {
            try
            {
                if (Program.Config.LastSearch != null)
                {
                    txtSeedPhrase.Text = Program.Config.LastSearch.LastSeedPhrase;
                    txtBitcoinAddress.Text = Program.Config.LastSearch.LastBitcoinAddress;
                    numWordCount.Value = Program.Config.LastSearch.LastWordCount;
                    chkFullSearch.Checked = Program.Config.LastSearch.LastFullSearch;
                    numThreadCount.Value = Program.Config.LastSearch.LastThreadCount;
                }

                ValidateInputs();
                AddLog("📋 Конфигурация загружена");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка загрузки конфигурации: {ex.Message}");
            }
        }

        private void SaveFormConfig()
        {
            try
            {
                Program.Config.LastSearch.LastSeedPhrase = txtSeedPhrase.Text;
                Program.Config.LastSearch.LastBitcoinAddress = txtBitcoinAddress.Text;
                Program.Config.LastSearch.LastWordCount = (int)numWordCount.Value;
                Program.Config.LastSearch.LastFullSearch = chkFullSearch.Checked;
                Program.Config.LastSearch.LastThreadCount = (int)numThreadCount.Value;
                Program.Config.LastSearch.LastSearchTime = DateTime.Now;

                Program.SaveConfig();
                AddLog("📋 Конфигурация сохранена");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка сохранения конфигурации: {ex.Message}");
            }
        }

        #endregion

        #region Вспомогательные методы

        private void AddLog(string message)
        {
            try
            {
                if (txtLog.InvokeRequired)
                {
                    txtLog.BeginInvoke(() => AddLog(message));
                    return;
                }

                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                txtLog.AppendText($"[{timestamp}] {message}\r\n");
                
                // Автопрокрутка
                txtLog.SelectionStart = txtLog.Text.Length;
                txtLog.ScrollToCaret();

                // Ограничиваем размер лога (последние 500 строк)
                if (txtLog.Lines.Length > 500)
                {
                    var lines = txtLog.Lines.Skip(250).ToArray();
                    txtLog.Text = string.Join("\r\n", lines);
                }
            }
            catch
            {
                // Игнорируем ошибки логирования
            }
        }

        #endregion

        #region Очистка ресурсов

        private void StandaloneForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                // Останавливаем поиск
                StopSearch();
                
                // Сохраняем конфигурацию
                SaveFormConfig();
                
                // Останавливаем таймеры
                autoSaveTimer?.Stop();
                autoSaveTimer?.Dispose();
                
                // Очищаем ресурсы
                searchWorker?.Dispose();
                
                AddLog("👋 Приложение завершает работу");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при закрытии формы: {ex.Message}");
            }
        }

        #endregion
    }
} 