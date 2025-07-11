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
        private SeedPhraseFinder? finder;
        private bool isSearching = false;
        private BackgroundWorker? searchWorker;

        // UI элементы для автономного поиска
        private TextBox txtSeedPhrase;
        private TextBox txtAddress;
        private Button btnSearch;
        private Button btnStop;
        private Label lblStatus;
        private TextBox txtLog;
        private ProgressBar progressBar;
        private Label lblProgress;
        private Label lblSpeed;
        private Label lblElapsed;
        private Label lblEstimated;
        private Button btnClearLog;
        private Button btnSaveResults;
        private NumericUpDown numThreads;

        public StandaloneForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(1000, 700);
            this.Text = "Bitcoin Finder - Автономный поиск";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(800, 500);

            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 4;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Параметры поиска
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Статус
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60F)); // Основная область
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40F)); // Лог

            // === СЕКЦИЯ ПАРАМЕТРОВ ПОИСКА ===
            var searchGroup = new GroupBox();
            searchGroup.Text = "Параметры поиска";
            searchGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            searchGroup.Dock = DockStyle.Fill;

            var searchLayout = new TableLayoutPanel();
            searchLayout.Dock = DockStyle.Fill;
            searchLayout.ColumnCount = 4;
            searchLayout.RowCount = 4;
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

            // Seed phrase
            searchLayout.Controls.Add(new Label { Text = "Seed phrase:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            txtSeedPhrase = new TextBox { Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            searchLayout.Controls.Add(txtSeedPhrase, 1, 0);

            // Bitcoin address
            searchLayout.Controls.Add(new Label { Text = "Bitcoin адрес:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 2, 0);
            txtAddress = new TextBox { Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            searchLayout.Controls.Add(txtAddress, 3, 0);

            // Threads
            searchLayout.Controls.Add(new Label { Text = "Потоки:", Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            numThreads = new NumericUpDown { Minimum = 1, Maximum = 32, Value = Environment.ProcessorCount, Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill };
            searchLayout.Controls.Add(numThreads, 1, 1);

            // Buttons
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            btnSearch = new Button { Text = "🔍 Начать поиск", Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.LightGreen, Size = new Size(120, 30) };
            btnSearch.Click += BtnSearch_Click;
            buttonPanel.Controls.Add(btnSearch);

            btnStop = new Button { Text = "⏹ Остановить", Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.LightCoral, Size = new Size(120, 30), Enabled = false };
            btnStop.Click += BtnStop_Click;
            buttonPanel.Controls.Add(btnStop);

            searchLayout.Controls.Add(buttonPanel, 3, 2);

            searchGroup.Controls.Add(searchLayout);
            mainLayout.Controls.Add(searchGroup, 0, 0);
            mainLayout.SetColumnSpan(searchGroup, 2);

            // === СЕКЦИЯ СТАТУСА ===
            var statusGroup = new GroupBox();
            statusGroup.Text = "Статус поиска";
            statusGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            statusGroup.Dock = DockStyle.Fill;

            var statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.ColumnCount = 5;
            statusLayout.RowCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

            // Статус
            lblStatus = new Label { Text = "Статус: Ожидание", Font = new Font("Segoe UI", 10F), ForeColor = Color.Blue, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblStatus, 0, 0);

            // Прогресс
            lblProgress = new Label { Text = "Обработано: 0", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblProgress, 1, 0);

            // Скорость
            lblSpeed = new Label { Text = "Скорость: 0/сек", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblSpeed, 2, 0);

            // Прошедшее время
            lblElapsed = new Label { Text = "Время: 00:00:00", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblElapsed, 3, 0);

            // Оставшееся время
            lblEstimated = new Label { Text = "Осталось: --:--:--", Font = new Font("Segoe UI", 10F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            statusLayout.Controls.Add(lblEstimated, 4, 0);

            // Прогресс бар
            progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 20 };
            statusLayout.SetColumnSpan(progressBar, 5);
            statusLayout.Controls.Add(progressBar, 0, 1);

            statusGroup.Controls.Add(statusLayout);
            mainLayout.Controls.Add(statusGroup, 0, 1);
            mainLayout.SetColumnSpan(statusGroup, 2);

            // === СЕКЦИЯ РЕЗУЛЬТАТОВ ===
            var resultsGroup = new GroupBox();
            resultsGroup.Text = "Результаты поиска";
            resultsGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            resultsGroup.Dock = DockStyle.Fill;

            var resultsLayout = new TableLayoutPanel();
            resultsLayout.Dock = DockStyle.Fill;
            resultsLayout.ColumnCount = 1;
            resultsLayout.RowCount = 2;
            resultsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            resultsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Область для результатов (пока пустая)
            var resultsPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            resultsLayout.Controls.Add(resultsPanel, 0, 0);

            // Кнопка сохранения
            btnSaveResults = new Button { Text = "💾 Сохранить результаты", Font = new Font("Segoe UI", 9F), Dock = DockStyle.Fill, Enabled = false };
            btnSaveResults.Click += BtnSaveResults_Click;
            resultsLayout.Controls.Add(btnSaveResults, 0, 1);

            resultsGroup.Controls.Add(resultsLayout);
            mainLayout.Controls.Add(resultsGroup, 0, 2);

            // === СЕКЦИЯ ЛОГА ===
            var logGroup = new GroupBox();
            logGroup.Text = "Лог поиска";
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
            mainLayout.Controls.Add(logGroup, 0, 3);

            this.Controls.Add(mainLayout);

            // Инициализация
            AddLog("=== АВТОНОМНЫЙ РЕЖИМ ЗАПУЩЕН ===");
            AddLog("Введите seed phrase и Bitcoin адрес для поиска");
        }

        private void BtnSearch_Click(object? sender, EventArgs e)
        {
            if (isSearching)
                return;

            // Валидация
            string seedPhrase = txtSeedPhrase.Text.Trim();
            string address = txtAddress.Text.Trim();

            if (string.IsNullOrWhiteSpace(seedPhrase))
            {
                MessageBox.Show("Введите seed phrase!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(address))
            {
                MessageBox.Show("Введите Bitcoin адрес!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Создаем параметры поиска
            var parameters = new SearchParameters
            {
                SeedPhrase = seedPhrase,
                BitcoinAddress = address,
                WordCount = 12, // или можно добавить выбор
                FullSearch = false, // или добавить чекбокс
                ThreadCount = (int)numThreads.Value
            };

            finder = new SeedPhraseFinder();
            searchWorker = new BackgroundWorker();
            searchWorker.WorkerReportsProgress = true;
            searchWorker.WorkerSupportsCancellation = true;
            searchWorker.DoWork += (s, args) => finder.Search(parameters, searchWorker, args);
            searchWorker.ProgressChanged += SearchWorker_ProgressChanged;
            searchWorker.RunWorkerCompleted += SearchWorker_RunWorkerCompleted;

            isSearching = true;
            btnSearch.Enabled = false;
            btnStop.Enabled = true;
            lblStatus.Text = "Статус: Поиск...";
            lblStatus.ForeColor = Color.Green;
            progressBar.Style = ProgressBarStyle.Marquee;

            AddLog($"Начинаем поиск: {seedPhrase} -> {address}");
            AddLog($"Потоки: {parameters.ThreadCount}");

            searchWorker.RunWorkerAsync();
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            if (searchWorker != null && isSearching)
            {
                searchWorker.CancelAsync();
                AddLog("Поиск остановлен пользователем");
                isSearching = false;
                btnSearch.Enabled = true;
                btnStop.Enabled = false;
                lblStatus.Text = "Статус: Остановлено";
                lblStatus.ForeColor = Color.Orange;
            }
        }

        private void SearchWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is string message)
            {
                AddLog(message);
            }
            if (e.ProgressPercentage > 0 && e.ProgressPercentage <= 100)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Min(e.ProgressPercentage, 100);
            }
        }

        private void SearchWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            isSearching = false;
            btnSearch.Enabled = true;
            btnStop.Enabled = false;
            lblStatus.Text = "Статус: Завершено";
            lblStatus.ForeColor = Color.Blue;
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 100;
            AddLog("Поиск завершен");
        }

        private void UpdateProgress(long current, long total, double rate)
        {
            lblProgress.Text = $"Обработано: {current:N0}";
            lblSpeed.Text = $"Скорость: {rate:F0}/сек";

            if (total > 0)
            {
                var percentage = (int)((double)current / total * 100);
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Min(percentage, 100);
                
                if (rate > 0)
                {
                    var remaining = total - current;
                    var estimatedSeconds = remaining / rate;
                    var estimatedTime = TimeSpan.FromSeconds(estimatedSeconds);
                    lblEstimated.Text = $"Осталось: {estimatedTime:hh\\:mm\\:ss}";
                }
            }
        }

        private void OnFound(SearchResult result)
        {
            AddLog($"🎉 НАЙДЕНО! Индекс: {result.FoundAtIndex:N0}");
            AddLog($"Seed phrase: {result.SeedPhrase}");
            AddLog($"Адрес: {result.BitcoinAddress}");
            
            btnSaveResults.Enabled = true;
        }

        private void AddLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AddLog(message)));
                return;
            }

            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private void BtnClearLog_Click(object? sender, EventArgs e)
        {
            txtLog.Clear();
            AddLog("Лог очищен");
        }

        private void BtnSaveResults_Click(object? sender, EventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                    FileName = $"bitcoin_finder_results_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(saveDialog.FileName, txtLog.Text);
                    MessageBox.Show("Результаты сохранены!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    AddLog($"Результаты сохранены в файл: {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AddLog($"Ошибка сохранения: {ex.Message}");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (searchWorker != null && isSearching)
            {
                searchWorker.CancelAsync();
            }
            base.OnFormClosing(e);
        }
    }
} 