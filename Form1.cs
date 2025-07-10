using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NBitcoin;
using System.IO;
using System.Text.Json;
using System.Net.Sockets;
using System.Threading;

namespace BitcoinFinder
{
    public partial class Form1 : Form
    {
        private BackgroundWorker? backgroundWorker;
        private bool isSearching = false;
        private List<string> bip39Words = new List<string>();
        private double lastDisplayedSpeed = 0;

        // Объявляем все элементы управления
        private Label lblSeedPhrase;
        private TextBox txtSeedPhrase;
        private Label lblBitcoinAddress;
        private TextBox txtBitcoinAddress;
        private ComboBox cmbWordCount;
        private Label lblWordCount;
        private CheckBox chkFullSearch;
        private Label lblThreads;
        private NumericUpDown numThreads;
        private Button btnSearch;
        private Button btnStop;
        private Button btnSaveProgress;
        private Button btnLoadProgress;
        private ProgressBar progressBar;
        private TextBox txtResults;
        private Label lblResults;
        private Label lblStatus;
        private Label lblProgress;
        private Label lblSpeed;
        private Label lblCurrentPhrase;
        private Label lblTimeLeft;
        private ListBox listBoxCurrentPhrases;
        private Label lblThreadsList;
        private ToolTip toolTip1;
        private TableLayoutPanel tableProgress;
        private TableLayoutPanel tableInput;
        private TableLayoutPanel tableStatus;
        private ToolTip toolTipPhrase;
        private TableLayoutPanel tableSpeedTime;
        private Button btnCopyPhrase;
        private Label lblProgressStatus;

        private ProgressData? loadedProgressData = null;
        private bool isProgressLoaded = false;
        private const string LastProgressFile = "bitcoin_finder_progress_last.json";
        private bool isProgressLoadedForSearch = false;

        // Флаги для временного отключения обработчиков событий
        private bool suppressParamEvents = false;
        private BigInteger currentCombination = 0;
        private BigInteger totalCombinations = 0;
        private AdvancedSeedPhraseFinder advancedFinder;
        private System.Windows.Forms.Timer autoSaveTimer;
        private string telemetryLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"bitcoin_finder_telemetry_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // --- Для режима агента ---
        private CheckBox chkAgentMode;
        private TextBox txtAgentIp;
        private TextBox txtAgentPort;
        private Button btnAgentConnect;
        private Label lblAgentStatus;
        private bool isAgentConnected = false;
        private CancellationTokenSource? agentCts;
        private Task? agentTask;

        public Form1()
        {
            InitializeComponent();
            advancedFinder = new AdvancedSeedPhraseFinder();
            LoadBIP39Words();
            SetupBackgroundWorker();
            // --- UI для режима агента ---
            chkAgentMode = new CheckBox();
            chkAgentMode.Text = "Работать как агент (подключаться к серверу)";
            chkAgentMode.Font = new Font("Segoe UI", 11F);
            chkAgentMode.Dock = DockStyle.Top;
            chkAgentMode.CheckedChanged += ChkAgentMode_CheckedChanged;
            txtAgentIp = new TextBox();
            txtAgentIp.Width = 120;
            txtAgentIp.Text = "127.0.0.1";
            txtAgentPort = new TextBox();
            txtAgentPort.Width = 60;
            txtAgentPort.Text = "5000";
            btnAgentConnect = new Button();
            btnAgentConnect.Text = "Подключиться";
            btnAgentConnect.Width = 120;
            btnAgentConnect.Click += BtnAgentConnect_Click;
            lblAgentStatus = new Label();
            lblAgentStatus.Text = "Статус: Отключено";
            lblAgentStatus.Font = new Font("Segoe UI", 10F);
            lblAgentStatus.Dock = DockStyle.Top;
            // --- Добавляем в форму ---
            var agentPanel = new FlowLayoutPanel();
            agentPanel.Dock = DockStyle.Top;
            agentPanel.Height = 40;
            agentPanel.Controls.Add(chkAgentMode);
            agentPanel.Controls.Add(new Label { Text = "IP сервера:", AutoSize = true, Font = new Font("Segoe UI", 11F) });
            agentPanel.Controls.Add(txtAgentIp);
            agentPanel.Controls.Add(new Label { Text = "Порт:", AutoSize = true, Font = new Font("Segoe UI", 11F) });
            agentPanel.Controls.Add(txtAgentPort);
            agentPanel.Controls.Add(btnAgentConnect);
            agentPanel.Controls.Add(lblAgentStatus);
            Controls.Add(agentPanel);
            // --- Автозаполнение адреса из конфига ---
            if (string.IsNullOrWhiteSpace(txtBitcoinAddress.Text))
            {
                txtBitcoinAddress.Text = Program.Config.DefaultBitcoinAddress;
            }
            txtBitcoinAddress.TextChanged += TxtBitcoinAddress_TextChanged_SaveConfig;
            // Подписка на сброс прогресса при изменении параметров
            txtSeedPhrase.TextChanged += AnySearchParamChanged;
            txtBitcoinAddress.TextChanged += AnySearchParamChanged;
            cmbWordCount.SelectedIndexChanged += AnySearchParamChanged;
            chkFullSearch.CheckedChanged += AnySearchParamChanged;
            numThreads.ValueChanged += AnySearchParamChanged;
            // --- Автозагрузка последнего прогресса ---
            TryAutoLoadLastProgress();
            // Добавляем лейбл для последней проверенной фразы
            // lblLastPhrase = new Label(); // Удален
            // lblLastPhrase.Font = new Font("Consolas", 10F); // Удален
            // lblLastPhrase.Dock = DockStyle.Top; // Удален
            // lblLastPhrase.AutoSize = true; // Удален
            // lblLastPhrase.Text = "Последний приватный ключ: —"; // Удален
            // Controls.Add(lblLastPhrase); // Удален
            // lblProgressStatus теперь инициализируется только в InitializeComponent
            this.FormClosing += Form1_FormClosing;
            autoSaveTimer = new System.Windows.Forms.Timer();
            autoSaveTimer.Interval = 60000; // 1 минута
            autoSaveTimer.Tick += (s, e) => SaveProgressFromForm();
            autoSaveTimer.Start();
        }

        private void LoadBIP39Words()
        {
            // Загружаем BIP39 словарь
            bip39Words = new Mnemonic(Wordlist.English).WordList.GetWords().ToList();
        }

        private void SetupBackgroundWorker()
        {
            backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
            backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
        }

        private void InitializeComponent()
        {
            // Явная инициализация всех элементов управления
            lblSeedPhrase = new Label();
            txtSeedPhrase = new TextBox();
            lblBitcoinAddress = new Label();
            txtBitcoinAddress = new TextBox();
            cmbWordCount = new ComboBox();
            lblWordCount = new Label();
            chkFullSearch = new CheckBox();
            lblThreads = new Label();
            numThreads = new NumericUpDown();
            btnSearch = new Button();
            btnStop = new Button();
            btnSaveProgress = new Button();
            btnLoadProgress = new Button();
            progressBar = new ProgressBar();
            txtResults = new TextBox();
            lblResults = new Label();
            lblStatus = new Label();
            lblProgress = new Label();
            lblSpeed = new Label();
            lblCurrentPhrase = new Label();
            lblTimeLeft = new Label();
            listBoxCurrentPhrases = new ListBox();
            lblThreadsList = new Label();
            toolTip1 = new ToolTip();
            toolTipPhrase = new ToolTip();
            btnCopyPhrase = new Button();
            lblProgressStatus = new Label();
            lblProgressStatus.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblProgressStatus.Dock = DockStyle.Top;
            lblProgressStatus.AutoSize = true;
            lblProgressStatus.Text = "";
            lblProgressStatus.Visible = false;
            // --- Главная сетка ---
            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.RowStyles.Clear();
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 13F)); // Параметры (уменьшено)
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 13F)); // Кнопки
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 14F)); // Прогресс
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25F)); // Потоки
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F)); // Результаты
            this.Controls.Clear();
            this.Controls.Add(mainLayout);

            // --- 1. Параметры поиска ---
            var groupParams = new GroupBox();
            groupParams.Text = "Параметры поиска";
            groupParams.Dock = DockStyle.Fill;
            groupParams.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            var paramsLayout = new TableLayoutPanel();
            paramsLayout.Dock = DockStyle.Fill;
            paramsLayout.ColumnCount = 6;
            paramsLayout.RowCount = 2;
            paramsLayout.ColumnStyles.Clear();
            paramsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Label1
            paramsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F)); // TextBox1
            paramsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // CheckBox/Label2
            paramsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F)); // TextBox2
            paramsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Label3
            paramsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F)); // TextBox3/ComboBox
            paramsLayout.RowStyles.Clear();
            paramsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            paramsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            // Первая строка: Seed-фраза, поле, чекбокс, пусто, пусто, пусто
            lblSeedPhrase.Text = "Seed-фраза:";
            lblSeedPhrase.TextAlign = ContentAlignment.MiddleRight;
            lblSeedPhrase.Font = new Font("Segoe UI", 10F);
            lblSeedPhrase.AutoSize = true;
            lblSeedPhrase.Dock = DockStyle.Fill;
            paramsLayout.Controls.Add(lblSeedPhrase, 0, 0);
            txtSeedPhrase.Font = new Font("Segoe UI", 10F);
            txtSeedPhrase.Dock = DockStyle.Fill;
            txtSeedPhrase.MinimumSize = new Size(120, 0);
            txtSeedPhrase.PlaceholderText = "Введите seed-фразу или * для перебора";
            paramsLayout.Controls.Add(txtSeedPhrase, 1, 0);
            chkFullSearch.Text = "Полный перебор";
            chkFullSearch.Font = new Font("Segoe UI", 10F);
            chkFullSearch.Dock = DockStyle.Left;
            chkFullSearch.AutoSize = true;
            chkFullSearch.CheckedChanged += ChkFullSearch_CheckedChanged;
            paramsLayout.Controls.Add(chkFullSearch, 2, 0);
            // Вторая строка: Биткоин-адрес, поле, Потоков, число, Количество слов, ComboBox
            lblBitcoinAddress.Text = "Биткоин-адрес:";
            lblBitcoinAddress.TextAlign = ContentAlignment.MiddleRight;
            lblBitcoinAddress.Font = new Font("Segoe UI", 10F);
            lblBitcoinAddress.AutoSize = true;
            lblBitcoinAddress.Dock = DockStyle.Fill;
            paramsLayout.Controls.Add(lblBitcoinAddress, 0, 1);
            txtBitcoinAddress.Font = new Font("Segoe UI", 10F);
            txtBitcoinAddress.Dock = DockStyle.Fill;
            txtBitcoinAddress.MinimumSize = new Size(120, 0);
            txtBitcoinAddress.PlaceholderText = "Введите адрес для поиска";
            paramsLayout.Controls.Add(txtBitcoinAddress, 1, 1);
            lblThreads.Text = "Потоков:";
            lblThreads.TextAlign = ContentAlignment.MiddleRight;
            lblThreads.Font = new Font("Segoe UI", 10F);
            lblThreads.AutoSize = true;
            lblThreads.Dock = DockStyle.Fill;
            paramsLayout.Controls.Add(lblThreads, 2, 1);
            numThreads.Font = new Font("Segoe UI", 10F);
            numThreads.MinimumSize = new Size(40, 0);
            numThreads.Dock = DockStyle.Fill;
            paramsLayout.Controls.Add(numThreads, 3, 1);
            lblWordCount.Text = "Количество слов:";
            lblWordCount.TextAlign = ContentAlignment.MiddleRight;
            lblWordCount.Font = new Font("Segoe UI", 10F);
            lblWordCount.AutoSize = true;
            lblWordCount.Dock = DockStyle.Fill;
            paramsLayout.Controls.Add(lblWordCount, 4, 1);
            cmbWordCount.Font = new Font("Segoe UI", 10F);
            cmbWordCount.MinimumSize = new Size(40, 0);
            cmbWordCount.Dock = DockStyle.Fill;
            cmbWordCount.DropDownStyle = ComboBoxStyle.DropDownList;
            paramsLayout.Controls.Add(cmbWordCount, 5, 1);
            groupParams.Controls.Add(paramsLayout);
            mainLayout.Controls.Add(groupParams, 0, 0);

            // --- 2. Кнопки ---
            var buttonGroup = new GroupBox();
            buttonGroup.Dock = DockStyle.Fill;
            buttonGroup.Text = "Действия";
            buttonGroup.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.LeftToRight;
            buttonPanel.WrapContents = true;
            buttonPanel.AutoSize = true;
            buttonPanel.Padding = new Padding(10, 10, 10, 10);
            btnSearch.Text = "Поиск";
            btnSearch.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            btnSearch.Width = 110;
            btnSearch.Height = 32;
            btnSearch.BackColor = Color.LightGreen;
            btnStop.Text = "Стоп";
            btnStop.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            btnStop.Width = 110;
            btnStop.Height = 32;
            btnStop.BackColor = Color.LightCoral;
            btnSaveProgress.Text = "Сохранить";
            btnSaveProgress.Font = new Font("Segoe UI", 11F);
            btnSaveProgress.Width = 110;
            btnSaveProgress.Height = 32;
            btnLoadProgress.Text = "Загрузить";
            btnLoadProgress.Font = new Font("Segoe UI", 11F);
            btnLoadProgress.Width = 110;
            btnLoadProgress.Height = 32;
            btnCopyPhrase.Text = "Копировать";
            btnCopyPhrase.Font = new Font("Segoe UI", 11F);
            btnCopyPhrase.Width = 110;
            btnCopyPhrase.Height = 32;
            buttonPanel.Controls.Add(btnSearch);
            buttonPanel.Controls.Add(btnStop);
            buttonPanel.Controls.Add(btnSaveProgress);
            buttonPanel.Controls.Add(btnLoadProgress);
            buttonPanel.Controls.Add(btnCopyPhrase);
            buttonGroup.Controls.Add(buttonPanel);
            mainLayout.Controls.Add(buttonGroup, 0, 1);

            // --- 3. Прогресс ---
            var groupProgress = new GroupBox();
            groupProgress.Text = "Прогресс";
            groupProgress.Dock = DockStyle.Fill;
            groupProgress.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            var progressLayout = new TableLayoutPanel();
            progressLayout.Dock = DockStyle.Fill;
            progressLayout.ColumnCount = 4;
            progressLayout.RowCount = 2;
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // Первая строка: Проверено, Скорость, Осталось, Статус
            lblProgress.Font = new Font("Segoe UI", 14F);
            lblProgress.AutoSize = true;
            lblProgress.Dock = DockStyle.Fill;
            progressLayout.Controls.Add(lblProgress, 0, 0);
            lblSpeed.Font = new Font("Segoe UI", 14F);
            lblSpeed.AutoSize = true;
            lblSpeed.Dock = DockStyle.Fill;
            progressLayout.Controls.Add(lblSpeed, 1, 0);
            lblTimeLeft.Font = new Font("Segoe UI", 14F);
            lblTimeLeft.AutoSize = true;
            lblTimeLeft.Dock = DockStyle.Fill;
            progressLayout.Controls.Add(lblTimeLeft, 2, 0);
            lblStatus.Font = new Font("Segoe UI", 14F);
            lblStatus.AutoSize = true;
            lblStatus.Dock = DockStyle.Fill;
            progressLayout.Controls.Add(lblStatus, 3, 0);
            // Вторая строка: прогрессбар на всю ширину
            progressBar.Dock = DockStyle.Fill;
            progressBar.Height = 32;
            progressLayout.SetColumnSpan(progressBar, 4);
            progressLayout.Controls.Add(progressBar, 0, 1);
            // Третья строка: статус восстановления прогресса
            progressLayout.Controls.Add(lblProgressStatus, 0, 2);
            progressLayout.SetColumnSpan(lblProgressStatus, 4);
            groupProgress.Controls.Add(progressLayout);
            mainLayout.Controls.Add(groupProgress, 0, 2);

            // --- 4. Потоки ---
            var groupThreads = new GroupBox();
            groupThreads.Text = "Потоки (каждый поток — текущая фраза)";
            groupThreads.Dock = DockStyle.Fill;
            groupThreads.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            var threadsLayout = new TableLayoutPanel();
            threadsLayout.Dock = DockStyle.Fill;
            threadsLayout.ColumnCount = 1;
            threadsLayout.RowCount = 2;
            threadsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            threadsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            lblCurrentPhrase.Font = new Font("Segoe UI", 14F);
            lblCurrentPhrase.AutoSize = true;
            lblCurrentPhrase.Dock = DockStyle.Fill;
            threadsLayout.Controls.Add(lblCurrentPhrase, 0, 0);
            listBoxCurrentPhrases.Dock = DockStyle.Fill;
            listBoxCurrentPhrases.Font = new Font("Consolas", 14F);
            threadsLayout.Controls.Add(listBoxCurrentPhrases, 0, 1);
            groupThreads.Controls.Add(threadsLayout);
            mainLayout.Controls.Add(groupThreads, 0, 3);

            // --- 5. Результаты ---
            var groupResults = new GroupBox();
            groupResults.Text = "Результаты поиска";
            groupResults.Dock = DockStyle.Fill;
            groupResults.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            txtResults.Dock = DockStyle.Fill;
            txtResults.Multiline = true;
            txtResults.ScrollBars = ScrollBars.Vertical;
            txtResults.Font = new Font("Consolas", 14F);
            groupResults.Controls.Add(txtResults);
            mainLayout.Controls.Add(groupResults, 0, 4);

            // --- Настройка формы ---
            this.MinimumSize = new Size(1100, 900);
            this.Size = new Size(1300, 1000);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Bitcoin Seed Phrase Finder";

            // --- Обработчики событий и тултипы ---
            btnSearch.Click += BtnSearch_Click;
            btnStop.Click += BtnStop_Click;
            btnSaveProgress.Click += BtnSaveProgress_Click;
            btnLoadProgress.Click += BtnLoadProgress_Click;
            btnCopyPhrase.Click += (s, e) => {
                if (!string.IsNullOrEmpty(lblCurrentPhrase.Text))
                {
                    Clipboard.SetText(lblCurrentPhrase.Text.Replace("Текущая фраза:", "").Trim());
                }
            };
            toolTip1.SetToolTip(btnSearch, "Запустить поиск по введённым параметрам");
            toolTip1.SetToolTip(btnStop, "Остановить текущий поиск");
            toolTip1.SetToolTip(btnSaveProgress, "Сохранить текущий прогресс поиска в файл");
            toolTip1.SetToolTip(btnLoadProgress, "Загрузить прогресс поиска из файла");
            toolTip1.SetToolTip(btnCopyPhrase, "Скопировать текущую проверяемую фразу");
            toolTip1.SetToolTip(chkFullSearch, "Если не помните фразу — включите полный перебор. Вместо слова используйте *");
            // --- Заполнение ComboBox и NumericUpDown ---
            cmbWordCount.Items.Clear();
            cmbWordCount.Items.Add("Авто (все варианты)");
            cmbWordCount.Items.AddRange(new object[] { 12, 15, 18, 21, 24 });
            cmbWordCount.SelectedIndex = 0;
            toolTip1.SetToolTip(cmbWordCount, "Если не знаете длину — выберите 'Авто'. Будет перебор по всем вариантам, но это дольше.");
            numThreads.Minimum = 1;
            numThreads.Maximum = 32;
            numThreads.Value = 4;
            // --- Placeholder’ы и подписи для всех полей ---
            txtSeedPhrase.PlaceholderText = "Введите seed-фразу или * для перебора";
            txtBitcoinAddress.PlaceholderText = "Введите адрес для поиска";
            // --- Инициализация статусной панели ---
            lblProgress.Text = "Проверено: 0 из 0";
            lblSpeed.Text = "Скорость: 0/сек";
            lblTimeLeft.Text = "Осталось: 00:00:00";
            lblStatus.Text = "Готов к поиску";
            lblCurrentPhrase.Text = "Текущая фраза: —";
            // --- Placeholder для списка потоков и результатов ---
            listBoxCurrentPhrases.Items.Clear();
            listBoxCurrentPhrases.Items.Add("Потоки будут отображаться здесь");
            txtResults.Text = "Здесь будут результаты поиска";
            // --- Валидация и блокировка кнопок ---
            btnStop.Enabled = false;
            btnSearch.Enabled = false;
            txtSeedPhrase.TextChanged += ValidateSearchFields;
            txtBitcoinAddress.TextChanged += ValidateSearchFields;
            cmbWordCount.SelectedIndexChanged += ValidateSearchFields;
            numThreads.ValueChanged += ValidateSearchFields;
        }

        private void SetupControls()
        {
            // Создаем шрифт 12pt для лучшей читаемости
            Font largeFont = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular);
            Font largeBoldFont = new Font("Microsoft Sans Serif", 12F, FontStyle.Bold);
            
            // lblSeedPhrase
            this.lblSeedPhrase.AutoSize = true;
            this.lblSeedPhrase.Location = new Point(12, 15);
            this.lblSeedPhrase.Name = "lblSeedPhrase";
            this.lblSeedPhrase.Size = new Size(200, 15);
            this.lblSeedPhrase.Text = "Seed фраза (через пробел, * для неизвестных):";
            this.lblSeedPhrase.Font = largeFont;

            // txtSeedPhrase
            this.txtSeedPhrase.Location = new Point(12, 35);
            this.txtSeedPhrase.Name = "txtSeedPhrase";
            this.txtSeedPhrase.Size = new Size(600, 25);
            this.txtSeedPhrase.Text = "* * * * * * * * * * * *";
            this.txtSeedPhrase.PlaceholderText = "Введите известные слова, * для неизвестных";
            this.txtSeedPhrase.Font = largeFont;

            // lblBitcoinAddress
            this.lblBitcoinAddress.AutoSize = true;
            this.lblBitcoinAddress.Location = new Point(12, 70);
            this.lblBitcoinAddress.Name = "lblBitcoinAddress";
            this.lblBitcoinAddress.Size = new Size(150, 15);
            this.lblBitcoinAddress.Text = "Биткоин адрес:";
            this.lblBitcoinAddress.Font = largeFont;

            // txtBitcoinAddress
            this.txtBitcoinAddress.Location = new Point(12, 90);
            this.txtBitcoinAddress.Name = "txtBitcoinAddress";
            this.txtBitcoinAddress.Size = new Size(400, 25);
            this.txtBitcoinAddress.Text = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ";
            this.txtBitcoinAddress.PlaceholderText = "Введите биткоин адрес для поиска";
            this.txtBitcoinAddress.Font = largeFont;

            // lblWordCount
            this.lblWordCount.AutoSize = true;
            this.lblWordCount.Location = new Point(12, 125);
            this.lblWordCount.Name = "lblWordCount";
            this.lblWordCount.Size = new Size(100, 15);
            this.lblWordCount.Text = "Количество слов:";
            this.lblWordCount.Font = largeFont;

            // cmbWordCount
            this.cmbWordCount.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbWordCount.Location = new Point(12, 145);
            this.cmbWordCount.Name = "cmbWordCount";
            this.cmbWordCount.Size = new Size(100, 25);
            this.cmbWordCount.Items.AddRange(new object[] { "12", "18", "24" });
            this.cmbWordCount.SelectedIndex = 0;
            this.cmbWordCount.Font = largeFont;
            this.cmbWordCount.SelectedIndexChanged += (s, e) => GenerateRandomSeedPhrase();

            // chkFullSearch
            this.chkFullSearch.AutoSize = true;
            this.chkFullSearch.Location = new Point(130, 145);
            this.chkFullSearch.Name = "chkFullSearch";
            this.chkFullSearch.Size = new Size(200, 19);
            this.chkFullSearch.Text = "Полный перебор (не помню фразу)";
            this.chkFullSearch.Font = largeFont;
            this.chkFullSearch.CheckedChanged += ChkFullSearch_CheckedChanged;

            // lblThreads
            this.lblThreads.AutoSize = true;
            this.lblThreads.Location = new Point(12, 175);
            this.lblThreads.Name = "lblThreads";
            this.lblThreads.Size = new Size(80, 15);
            this.lblThreads.Text = "Потоки:";
            this.lblThreads.Font = largeFont;

            // numThreads
            this.numThreads.Location = new Point(12, 195);
            this.numThreads.Name = "numThreads";
            this.numThreads.Size = new Size(80, 25);
            this.numThreads.Minimum = 1;
            this.numThreads.Maximum = Environment.ProcessorCount;
            this.numThreads.Value = Math.Min(Environment.ProcessorCount, 8);
            this.numThreads.Font = largeFont;

            // btnSearch
            this.btnSearch.Location = new Point(12, 225);
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.Size = new Size(100, 48);
            this.btnSearch.MinimumSize = new Size(0, 48);
            this.btnSearch.Padding = new Padding(0, 8, 0, 8);
            this.btnSearch.Anchor = AnchorStyles.Top;
            this.btnSearch.Text = "Начать поиск";
            this.btnSearch.Font = largeFont;
            this.btnSearch.Click += BtnSearch_Click;

            // btnStop
            this.btnStop.Location = new Point(120, 225);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new Size(100, 48);
            this.btnStop.MinimumSize = new Size(0, 48);
            this.btnStop.Padding = new Padding(0, 8, 0, 8);
            this.btnStop.Anchor = AnchorStyles.Top;
            this.btnStop.Text = "Остановить";
            this.btnStop.Font = largeFont;
            this.btnStop.Enabled = false;
            this.btnStop.Click += BtnStop_Click;

            // btnSaveProgress
            this.btnSaveProgress.Location = new Point(230, 225);
            this.btnSaveProgress.Name = "btnSaveProgress";
            this.btnSaveProgress.Size = new Size(100, 48);
            this.btnSaveProgress.MinimumSize = new Size(0, 48);
            this.btnSaveProgress.Padding = new Padding(0, 8, 0, 8);
            this.btnSaveProgress.Anchor = AnchorStyles.Top;
            this.btnSaveProgress.Text = "Сохранить";
            this.btnSaveProgress.Font = largeFont;
            this.btnSaveProgress.Click += BtnSaveProgress_Click;

            // btnLoadProgress
            this.btnLoadProgress.Location = new Point(340, 225);
            this.btnLoadProgress.Name = "btnLoadProgress";
            this.btnLoadProgress.Size = new Size(100, 48);
            this.btnLoadProgress.MinimumSize = new Size(0, 48);
            this.btnLoadProgress.Padding = new Padding(0, 8, 0, 8);
            this.btnLoadProgress.Anchor = AnchorStyles.Top;
            this.btnLoadProgress.Text = "Загрузить";
            this.btnLoadProgress.Font = largeFont;
            this.btnLoadProgress.Click += BtnLoadProgress_Click;

            // progressBar
            this.progressBar.Location = new Point(12, 265);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new Size(600, 23);

            // lblStatus
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new Point(12, 295);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(200, 15);
            this.lblStatus.Text = "Готов к поиску";
            this.lblStatus.Font = largeFont;

            // lblProgress
            this.lblProgress.AutoSize = true;
            this.lblProgress.Location = new Point(12, 275);
            this.lblProgress.Name = "lblProgress";
            this.lblProgress.Size = new Size(400, 18);
            this.lblProgress.Text = "ПРОВЕРЕНО: 0 из 0";
            this.lblProgress.Font = largeBoldFont;

            // lblResults
            this.lblResults.AutoSize = true;
            this.lblResults.Location = new Point(12, 350);
            this.lblResults.Name = "lblResults";
            this.lblResults.Size = new Size(100, 15);
            this.lblResults.Text = "Результаты:";
            this.lblResults.Font = largeFont;

            // txtResults
            this.txtResults.Location = new Point(12, 370);
            this.txtResults.Multiline = true;
            this.txtResults.Name = "txtResults";
            this.txtResults.ReadOnly = true;
            this.txtResults.ScrollBars = ScrollBars.Vertical;
            this.txtResults.Size = new Size(760, 280);
            this.txtResults.Font = new Font("Consolas", 9F);

            // listBoxCurrentPhrases
            this.listBoxCurrentPhrases.Location = new Point(12, 320);
            this.listBoxCurrentPhrases.Size = new Size(760, 60);
            this.listBoxCurrentPhrases.Name = "listBoxCurrentPhrases";
            this.listBoxCurrentPhrases.Font = largeFont;

            // lblSpeed
            this.lblSpeed.AutoSize = true;
            this.lblSpeed.Location = new Point(420, 275);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Size = new Size(200, 18);
            this.lblSpeed.Text = "СКОРОСТЬ: 0 в секунду";
            this.lblSpeed.Font = largeBoldFont;

            // lblCurrentPhrase
            this.lblCurrentPhrase.AutoSize = true;
            this.lblCurrentPhrase.Location = new Point(12, 295);
            this.lblCurrentPhrase.Name = "lblCurrentPhrase";
            this.lblCurrentPhrase.Size = new Size(600, 18);
            this.lblCurrentPhrase.Text = "Сейчас проверяем:";
            this.lblCurrentPhrase.Font = largeFont;

            // lblTimeLeft
            this.lblTimeLeft.AutoSize = true;
            this.lblTimeLeft.Location = new Point(620, 275);
            this.lblTimeLeft.Name = "lblTimeLeft";
            this.lblTimeLeft.Size = new Size(160, 18);
            this.lblTimeLeft.Text = "ОСТАЛОСЬ: 0 секунд";
            this.lblTimeLeft.Font = largeBoldFont;
        }

        private void GenerateRandomSeedPhrase()
        {
            int wordCount = 12;
            if (cmbWordCount != null && cmbWordCount.SelectedItem != null)
                int.TryParse(cmbWordCount.SelectedItem.ToString(), out wordCount);
            if (wordCount != 12 && wordCount != 15 && wordCount != 18 && wordCount != 21 && wordCount != 24)
                wordCount = 12;
            var random = new Random();
            var phrase = string.Join(" ", Enumerable.Range(0, wordCount)
                .Select(_ => bip39Words[random.Next(bip39Words.Count)]));
            txtSeedPhrase.Text = phrase;
        }

        private void BtnSearch_Click(object sender, EventArgs e)
        {
            if (isSearching) return;
            // Если был загружен прогресс — используем параметры из него
            if (isProgressLoaded && loadedProgressData != null)
            {
                txtSeedPhrase.Text = loadedProgressData.SeedPhrase;
                txtBitcoinAddress.Text = loadedProgressData.BitcoinAddress;
                cmbWordCount.Text = loadedProgressData.WordCount.ToString();
                chkFullSearch.Checked = loadedProgressData.FullSearch;
                numThreads.Value = loadedProgressData.ThreadCount;
            }
            else
            {
                UnlockSearchFields(); // Разблокировать поля для нового поиска
            }
            string seedPhrase = txtSeedPhrase.Text.Trim();
            string bitcoinAddress = txtBitcoinAddress.Text.Trim();
            bool fullSearch = chkFullSearch.Checked;
            var wordCounts = new List<int>();
            if (cmbWordCount.SelectedIndex == 0) // Авто
                wordCounts.AddRange(new[] { 12, 15, 18, 21, 24 });
            else
                wordCounts.Add(int.Parse(cmbWordCount.SelectedItem.ToString()!));

            // --- Если адрес не введён, используем из конфига ---
            if (string.IsNullOrWhiteSpace(bitcoinAddress))
            {
                bitcoinAddress = Program.Config.DefaultBitcoinAddress;
                txtBitcoinAddress.Text = bitcoinAddress;
            }
            // Валидация
            if (string.IsNullOrWhiteSpace(bitcoinAddress))
            {
                lblStatus.Text = "Введите биткоин-адрес!";
                MessageBox.Show("Введите биткоин-адрес!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!fullSearch && string.IsNullOrWhiteSpace(seedPhrase))
            {
                lblStatus.Text = "Введите seed-фразу!";
                MessageBox.Show("Введите seed-фразу!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Task.Run(async () =>
            {
                foreach (var wordCount in wordCounts)
                {
                    Invoke(new Action(() =>
                    {
                        lblStatus.Text = $"Поиск для {wordCount} слов...";
                        if (fullSearch)
                        {
                            txtSeedPhrase.ReadOnly = true;
                            txtSeedPhrase.Text = string.Join(" ", Enumerable.Repeat("*", wordCount));
                        }
                    }));
                    string curSeed = txtSeedPhrase.Text.Trim();
                    if (fullSearch) curSeed = string.Join(" ", Enumerable.Repeat("*", wordCount));
                    // Очищаем UI
                    Invoke(new Action(() =>
                    {
                        listBoxCurrentPhrases.Items.Clear();
                        txtResults.Clear();
                        lblProgress.Text = "Проверено: 0 из 0";
                        lblSpeed.Text = "Скорость: 0/сек";
                        lblTimeLeft.Text = "Осталось: 00:00:00";
                        lblCurrentPhrase.Text = "Текущая фраза: —";
                        progressBar.Value = 0;
            btnSearch.Enabled = false;
            btnStop.Enabled = true;
                        isSearching = true;
                    }));
                    var parameters = new SearchParameters
                    {
                        SeedPhrase = curSeed,
                BitcoinAddress = bitcoinAddress,
                        WordCount = wordCount,
                        FullSearch = fullSearch,
                        ThreadCount = (int)numThreads.Value,
                        ProgressFile = isProgressLoadedForSearch ? LastProgressFile : null
                    };
                    isProgressLoadedForSearch = false; // сбрасываем после запуска поиска
                    var tcs = new TaskCompletionSource<bool>();
                    void completed(object? s, RunWorkerCompletedEventArgs e)
                    {
                        backgroundWorker.RunWorkerCompleted -= completed;
                        tcs.SetResult(true);
                    }
                    backgroundWorker.RunWorkerCompleted += completed;
                    backgroundWorker.RunWorkerAsync(parameters);
                    await tcs.Task;
                    if (!isSearching) break; // Если пользователь нажал Стоп
                }
                Invoke(new Action(() =>
                {
                    isSearching = false;
                    btnSearch.Enabled = true;
                    btnStop.Enabled = false;
                    lblStatus.Text = "Поиск завершён";
                }));
            });
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            if (backgroundWorker?.IsBusy == true)
            {
                backgroundWorker.CancelAsync();
                lblStatus.Text = "Остановка...";
                btnStop.Enabled = false;
            }
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var searchParams = (SearchParameters)e.Argument!;
            var finder = new AdvancedSeedPhraseFinder();
            finder.Search(searchParams, backgroundWorker!, e);
        }

        private string FormatBigNumber(BigInteger value)
        {
            // Форматируем числа с пробелами для легкого чтения
            string numStr = value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            
            // Если число очень большое, добавляем сокращения
            if (value >= 1_000_000_000_000)
                return ((double)(value / 1_000_000_000_000)).ToString("0.##") + " триллионов";
            if (value >= 1_000_000_000)
                return ((double)(value / 1_000_000_000)).ToString("0.##") + " миллиардов";
            if (value >= 1_000_000)
                return ((double)(value / 1_000_000)).ToString("0.##") + " миллионов";
            if (value >= 1_000)
                return ((double)(value / 1_000)).ToString("0.##") + " тысяч";
            return numStr;
        }

        private string FormatTime(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays} дней {(int)ts.Hours} часов";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours} часов {(int)ts.Minutes} минут";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes} минут {(int)ts.Seconds} секунд";
            return $"{(int)ts.TotalSeconds} секунд";
        }
        private string TruncatePhrase(string? phrase, int maxLen)
        {
            if (string.IsNullOrEmpty(phrase)) return "";
            if (phrase.Length <= maxLen) return phrase;
            return phrase.Substring(0, maxLen - 3) + "...";
        }

        private string FormatSpeed(double value)
        {
            // Сглаживание: среднее между текущим и предыдущим значением
            double displayValue = value;
            if (lastDisplayedSpeed > 0)
                displayValue = (value + lastDisplayedSpeed) / 2.0;
            lastDisplayedSpeed = displayValue;
            if (displayValue < 1 && displayValue > 0)
                return "менее 1";
            if (displayValue >= 1_000_000_000)
                return (displayValue / 1_000_000_000).ToString("0.##") + " млрд";
            if (displayValue >= 1_000_000)
                return (displayValue / 1_000_000).ToString("0.##") + " млн";
            if (displayValue >= 1_000)
                return (displayValue / 1_000).ToString("0.##") + " тыс";
            if (displayValue > 0)
                return displayValue.ToString("N0");
            return "0";
        }

        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => BackgroundWorker_ProgressChanged(sender, e)));
                return;
            }
            if (e.UserState is string message)
            {
                txtResults.AppendText(message + Environment.NewLine);
                txtResults.SelectionStart = txtResults.Text.Length;
                txtResults.ScrollToCaret();
            }
            if (e.UserState is ProgressInfo progressInfo)
            {
                currentCombination = progressInfo.Current;
                totalCombinations = progressInfo.Total;
                string cur = FormatBigNumber(progressInfo.Current);
                string tot = FormatBigNumber(progressInfo.Total);
                lblProgress.Text = $"ПРОВЕРЕНО: {cur} из {tot} ({progressInfo.Percentage:F1}%)";
                lblSpeed.Text = $"СКОРОСТЬ: {FormatSpeed(progressInfo.Rate)} в секунду";
                lblTimeLeft.Text = $"ОСТАЛОСЬ: {FormatTime(progressInfo.Remaining)}";
                // Показываем приватный ключ вместо фразы
                if (!string.IsNullOrEmpty(progressInfo.CurrentPrivateKey))
                    lblCurrentPhrase.Text = $"Приватный ключ: {progressInfo.CurrentPrivateKey}";
                else
                    lblCurrentPhrase.Text = "Приватный ключ: —";
                lblStatus.Text = progressInfo.Status.Length > 120 ? progressInfo.Status.Substring(0, 120) + "..." : progressInfo.Status;
                // Обновляем список текущих фраз потоков
                listBoxCurrentPhrases.Items.Clear();
                string lastPhrase = null;
                if (progressInfo.CurrentPhrases != null && progressInfo.CurrentPhrases.Count > 0)
                {
                    for (int i = 0; i < progressInfo.CurrentPhrases.Count; i++)
                        listBoxCurrentPhrases.Items.Add($"Поток {i + 1}: {TruncatePhrase(progressInfo.CurrentPhrases[i] ?? "", 80)}");
                    if (listBoxCurrentPhrases.Items.Count > 0)
                    {
                        listBoxCurrentPhrases.SelectedIndex = listBoxCurrentPhrases.Items.Count - 1;
                    }
                    // Обновляем lblLastPhrase на актуальную последнюю фразу
                    lastPhrase = progressInfo.CurrentPhrases[0];
                }
                // --- Исправление: если массив пустой, используем последнее известное значение ---
                if (string.IsNullOrWhiteSpace(lastPhrase))
                {
                    // fallback: используем lblCurrentPhrase или lblLastPhrase
                    lastPhrase = lblCurrentPhrase.Text.Replace("Текущая фраза:", "").Trim();
                    if (string.IsNullOrWhiteSpace(lastPhrase) || lastPhrase == "—")
                        lastPhrase = "—"; // Удален lblLastPhrase.Text
                }
                // Удален lblLastPhrase.Text = $"Последний приватный ключ: {lastPhrase}";
                // Удален toolTipPhrase.SetToolTip(lblLastPhrase, lastPhrase);
            }
        }

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => BackgroundWorker_RunWorkerCompleted(sender, e)));
                return;
            }
            isSearching = false;
            btnSearch.Enabled = true;
            btnStop.Enabled = false;
            progressBar.Value = 100;
            if (e.Cancelled)
            {
                lblStatus.Text = "Поиск остановлен пользователем";
                txtResults.AppendText("Поиск остановлен пользователем" + Environment.NewLine);
            }
            else if (e.Error != null)
            {
                lblStatus.Text = "Ошибка: " + e.Error.Message;
                txtResults.AppendText("Ошибка: " + e.Error.Message + Environment.NewLine);
            }
            else
            {
                lblStatus.Text = "Поиск завершён";
                txtResults.AppendText("Поиск завершён" + Environment.NewLine);
            }
        }

        private void ChkFullSearch_CheckedChanged(object sender, EventArgs e)
        {
            if (chkFullSearch.Checked)
            {
                txtSeedPhrase.ReadOnly = true;
                int wordCount = 12;
                if (int.TryParse(cmbWordCount.Text, out int wc)) wordCount = wc;
                txtSeedPhrase.Text = string.Join(" ", Enumerable.Repeat("*", wordCount));
            }
            else
            {
                txtSeedPhrase.ReadOnly = false;
            }
            ValidateSearchFields(null, null);
        }

        private void BtnSaveProgress_Click(object sender, EventArgs e)
        {
            try
            {
                using (var saveDialog = new SaveFileDialog())
                {
                    saveDialog.Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*";
                    saveDialog.FileName = $"bitcoin_finder_progress_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    if (saveDialog.ShowDialog() == DialogResult.OK)
                    {
                        var progressData = new ProgressData
                        {
                            SeedPhrase = txtSeedPhrase.Text,
                            BitcoinAddress = txtBitcoinAddress.Text,
                            WordCount = int.Parse(cmbWordCount.Text),
                            FullSearch = chkFullSearch.Checked,
                            ThreadCount = (int)numThreads.Value,
                            Timestamp = DateTime.Now,
                            CurrentCombination = (isSearching ? currentCombination : loadedProgressData?.CurrentCombination != null ? BigInteger.Parse(loadedProgressData.CurrentCombination) : 0).ToString(),
                            TotalCombinations = (isSearching ? totalCombinations : loadedProgressData?.TotalCombinations != null ? BigInteger.Parse(loadedProgressData.TotalCombinations) : 0).ToString(),
                            LastCheckedPhrase = isSearching ? lblCurrentPhrase.Text.Replace("Текущая фраза:", "").Trim() : loadedProgressData?.LastCheckedPhrase ?? ""
                        };
                        var json = System.Text.Json.JsonSerializer.Serialize(progressData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(saveDialog.FileName, json);
                        File.WriteAllText(LastProgressFile, json);
                        MessageBox.Show("Прогресс сохранён!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        lblStatus.Text = "Прогресс сохранён";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Ошибка сохранения прогресса";
            }
        }

        private void BtnLoadProgress_Click(object sender, EventArgs e)
        {
            try
            {
                using (var openDialog = new OpenFileDialog())
                {
                    openDialog.Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*";
                    if (openDialog.ShowDialog() == DialogResult.OK)
                    {
                        var json = File.ReadAllText(openDialog.FileName);
                        var progressData = System.Text.Json.JsonSerializer.Deserialize<ProgressData>(json);
                        if (progressData != null)
                        {
                            suppressParamEvents = true;
                            txtSeedPhrase.Text = progressData.SeedPhrase ?? "";
                            txtBitcoinAddress.Text = progressData.BitcoinAddress ?? "";
                            cmbWordCount.Text = progressData.WordCount > 0 ? progressData.WordCount.ToString() : "12";
                            chkFullSearch.Checked = progressData.FullSearch;
                            numThreads.Value = progressData.ThreadCount > 0 ? progressData.ThreadCount : 4;
                            suppressParamEvents = false;
                            loadedProgressData = progressData;
                            isProgressLoaded = true;
                            isProgressLoadedForSearch = true;
                            BigInteger cur = BigInteger.Zero;
                            BigInteger tot = BigInteger.Zero;
                            BigInteger.TryParse(progressData.CurrentCombination, out cur);
                            BigInteger.TryParse(progressData.TotalCombinations, out tot);
                            double percent = tot > 0 ? ((double)cur * 100.0 / (double)tot) : 0;
                            lblProgress.Text = $"Проверено: {cur:N0} из {tot:N0} ({percent:F2}%)";
                            string lastPhrase = !string.IsNullOrWhiteSpace(progressData.LastCheckedPhrase) ? progressData.LastCheckedPhrase : "—";
                            lblStatus.Text = $"Готов к продолжению поиска с сохранённого места. Проверено: {cur:N0} из {tot:N0}. Последняя фраза: {lastPhrase}";
                            if (progressBar != null && tot > 0)
                            {
                                progressBar.Value = Math.Min((int)percent, 100);
                            }
                            if (!string.IsNullOrWhiteSpace(progressData.LastCheckedPhrase))
                            {
                                // Удален lblLastPhrase.Text = $"Последний приватный ключ: {progressData.LastCheckedPhrase}";
                                // Удален toolTipPhrase.SetToolTip(lblLastPhrase, progressData.LastCheckedPhrase);
                            }
                            else
                            {
                                // Удален lblLastPhrase.Text = "Последний приватный ключ: —";
                            }
                            // Блокируем поля
                            txtSeedPhrase.ReadOnly = true;
                            txtBitcoinAddress.ReadOnly = true;
                            cmbWordCount.Enabled = false;
                            chkFullSearch.Enabled = false;
                            numThreads.Enabled = false;
                            btnLoadProgress.Enabled = false;
                            bool restored = advancedFinder.IsProgressRestored;
                            if (restored)
                            {
                                lblProgressStatus.Text = "Прогресс успешно восстановлен";
                                lblProgressStatus.ForeColor = Color.Green;
                            }
                            else
                            {
                                lblProgressStatus.Text = "Прогресс не найден или параметры изменены";
                                lblProgressStatus.ForeColor = Color.Red;
                            }
                            lblProgressStatus.Visible = true;
                        }
                        else
                        {
                            MessageBox.Show("Ошибка: не удалось загрузить параметры из файла прогресса.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Ошибка загрузки прогресса";
            }
        }

        private void TryAutoLoadLastProgress()
        {
            try
            {
                if (File.Exists(LastProgressFile))
                {
                    var json = File.ReadAllText(LastProgressFile);
                    var progressData = System.Text.Json.JsonSerializer.Deserialize<ProgressData>(json);
                    if (progressData != null)
                    {
                        suppressParamEvents = true;
                        if (txtSeedPhrase != null) txtSeedPhrase.Text = progressData.SeedPhrase ?? "";
                        if (txtBitcoinAddress != null) txtBitcoinAddress.Text = progressData.BitcoinAddress ?? "";
                        if (cmbWordCount != null) cmbWordCount.Text = progressData.WordCount > 0 ? progressData.WordCount.ToString() : "12";
                        if (chkFullSearch != null) chkFullSearch.Checked = progressData.FullSearch;
                        if (numThreads != null) numThreads.Value = progressData.ThreadCount > 0 ? progressData.ThreadCount : 4;
                        suppressParamEvents = false;
                        loadedProgressData = progressData;
                        isProgressLoaded = true;
                        isProgressLoadedForSearch = true;
                        BigInteger cur = BigInteger.Zero;
                        BigInteger tot = BigInteger.Zero;
                        BigInteger.TryParse(progressData.CurrentCombination, out cur);
                        BigInteger.TryParse(progressData.TotalCombinations, out tot);
                        double percent = tot > 0 ? ((double)cur * 100.0 / (double)tot) : 0;
                        if (lblProgress != null) lblProgress.Text = $"Проверено: {cur:N0} из {tot:N0} ({percent:F2}%)";
                        string lastPhrase = !string.IsNullOrWhiteSpace(progressData.LastCheckedPhrase) ? progressData.LastCheckedPhrase : "—";
                        if (lblStatus != null) lblStatus.Text = $"Готов к продолжению поиска с сохранённого места. Проверено: {cur:N0} из {tot:N0}. Последняя фраза: {lastPhrase}";
                        if (progressBar != null && tot > 0)
                        {
                            progressBar.Value = Math.Min((int)percent, 100);
                        }
                        if (lblCurrentPhrase != null) // Удален lblLastPhrase
                        {
                            if (!string.IsNullOrWhiteSpace(progressData.LastCheckedPhrase))
                            {
                                // Удален lblLastPhrase.Text = $"Последний приватный ключ: {progressData.LastCheckedPhrase}";
                                // Удален toolTipPhrase?.SetToolTip(lblLastPhrase, progressData.LastCheckedPhrase);
                            }
                            else
                            {
                                // Удален lblLastPhrase.Text = "Последний приватный ключ: —";
                            }
                        }
                        // Блокируем поля
                        if (txtSeedPhrase != null) txtSeedPhrase.ReadOnly = true;
                        if (txtBitcoinAddress != null) txtBitcoinAddress.ReadOnly = true;
                        if (cmbWordCount != null) cmbWordCount.Enabled = false;
                        if (chkFullSearch != null) chkFullSearch.Enabled = false;
                        if (numThreads != null) numThreads.Enabled = false;
                        if (btnLoadProgress != null) btnLoadProgress.Enabled = false;
                    }
                    else
                    {
                        MessageBox.Show("Ошибка: не удалось загрузить параметры из файла прогресса.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка автозагрузки прогресса: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // Разблокировка полей при новом поиске или сбросе
        private void UnlockSearchFields()
        {
            txtSeedPhrase.ReadOnly = false;
            txtBitcoinAddress.ReadOnly = false;
            cmbWordCount.Enabled = true;
            chkFullSearch.Enabled = true;
            numThreads.Enabled = true;
            btnLoadProgress.Enabled = true;
            isProgressLoaded = false;
            loadedProgressData = null;
        }

        // При изменении любого из параметров — сбрасываем прогресс
        private void AnySearchParamChanged(object? sender, EventArgs e)
        {
            if (suppressParamEvents) return;
            if (isProgressLoaded)
            {
                var res = MessageBox.Show("Вы изменили параметры поиска. Прогресс будет сброшен. Продолжить?", "Внимание", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (res == DialogResult.OK)
                {
                    UnlockSearchFields();
                    lblStatus.Text = "Параметры изменены. Прогресс сброшен.";
                }
                else
                {
                    if (loadedProgressData != null)
                    {
                        suppressParamEvents = true;
                        txtSeedPhrase.Text = loadedProgressData.SeedPhrase;
                        txtBitcoinAddress.Text = loadedProgressData.BitcoinAddress;
                        cmbWordCount.Text = loadedProgressData.WordCount.ToString();
                        chkFullSearch.Checked = loadedProgressData.FullSearch;
                        numThreads.Value = loadedProgressData.ThreadCount;
                        suppressParamEvents = false;
                    }
                }
            }
        }

        private void ValidateSearchFields(object? sender, EventArgs e)
        {
            bool valid = true;
            string status = "Готов к поиску";
            if (string.IsNullOrWhiteSpace(txtBitcoinAddress.Text))
            {
                valid = false;
                status = "Введите биткоин-адрес!";
            }
            else if (!chkFullSearch.Checked && string.IsNullOrWhiteSpace(txtSeedPhrase.Text))
            {
                valid = false;
                status = "Введите seed-фразу!";
            }
            else if (cmbWordCount.SelectedIndex > 0 && !chkFullSearch.Checked)
            {
                int wordCount = int.Parse(cmbWordCount.SelectedItem.ToString()!);
                if (txtSeedPhrase.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != wordCount)
                {
                    valid = false;
                    status = $"Количество слов в фразе должно быть {wordCount}";
                }
            }
            btnSearch.Enabled = valid;
            lblStatus.Text = status;
        }

        private void TxtBitcoinAddress_TextChanged_SaveConfig(object? sender, EventArgs e)
        {
            var newAddress = txtBitcoinAddress.Text.Trim();
            if (!string.IsNullOrWhiteSpace(newAddress) && newAddress != Program.Config.DefaultBitcoinAddress)
            {
                Program.Config.DefaultBitcoinAddress = newAddress;
                Program.SaveConfig();
            }
        }

        private void SaveProgressFromForm()
        {
            try
            {
                var progressData = new ProgressData
                {
                    SeedPhrase = txtSeedPhrase.Text,
                    BitcoinAddress = txtBitcoinAddress.Text,
                    WordCount = int.Parse(cmbWordCount.Text),
                    FullSearch = chkFullSearch.Checked,
                    ThreadCount = (int)numThreads.Value,
                    Timestamp = DateTime.Now,
                    CurrentCombination = (isSearching ? currentCombination : loadedProgressData?.CurrentCombination != null ? BigInteger.Parse(loadedProgressData.CurrentCombination) : 0).ToString(),
                    TotalCombinations = (isSearching ? totalCombinations : loadedProgressData?.TotalCombinations != null ? BigInteger.Parse(loadedProgressData.TotalCombinations) : 0).ToString(),
                    LastCheckedPhrase = isSearching ? lblCurrentPhrase.Text.Replace("Текущая фраза:", "").Trim() : loadedProgressData?.LastCheckedPhrase ?? ""
                };
                var json = System.Text.Json.JsonSerializer.Serialize(progressData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LastProgressFile, json);
                // --- Запись в лог-файл ---
                var logLine = $"[{DateTime.Now:O}] PROGRESS_SNAPSHOT: {System.Text.Json.JsonSerializer.Serialize(progressData)}";
                File.AppendAllText(telemetryLogFile, logLine + Environment.NewLine);
            }
            catch { /* ignore errors */ }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveProgressFromForm();
        }

        private void ChkAgentMode_CheckedChanged(object? sender, EventArgs e)
        {
            bool agentMode = chkAgentMode.Checked;
            txtAgentIp.Enabled = agentMode;
            txtAgentPort.Enabled = agentMode;
            btnAgentConnect.Enabled = agentMode;
            // Блокируем обычные элементы поиска
            txtSeedPhrase.Enabled = !agentMode;
            txtBitcoinAddress.Enabled = !agentMode;
            cmbWordCount.Enabled = !agentMode;
            chkFullSearch.Enabled = !agentMode;
            numThreads.Enabled = !agentMode;
            btnSearch.Enabled = !agentMode;
            btnStop.Enabled = !agentMode;
            btnSaveProgress.Enabled = !agentMode;
            btnLoadProgress.Enabled = !agentMode;
        }

        private void BtnAgentConnect_Click(object? sender, EventArgs e)
        {
            if (!isAgentConnected)
            {
                string ip = txtAgentIp.Text.Trim();
                int port = int.TryParse(txtAgentPort.Text, out var p) ? p : 5000;
                agentCts = new CancellationTokenSource();
                agentTask = Task.Run(() => AgentWorker(ip, port, agentCts.Token));
                lblAgentStatus.Text = $"Статус: Подключение к {ip}:{port}...";
                btnAgentConnect.Text = "Отключиться";
            }
            else
            {
                agentCts?.Cancel();
                isAgentConnected = false;
                lblAgentStatus.Text = "Статус: Отключено";
                btnAgentConnect.Text = "Подключиться";
            }
        }
        private void AgentWorker(string ip, int port, CancellationToken token)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 10000;
                    client.Connect(ip, port);
                    isAgentConnected = true;
                    this.Invoke(new Action(() => lblAgentStatus.Text = $"Статус: Подключено к {ip}:{port}"));
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var finder = new AdvancedSeedPhraseFinder();
                        var agentClient = new DistributedAgentClient();
                        while (!token.IsCancellationRequested)
                        {
                            // Запросить задание
                            writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { command = "GET_TASK" }));
                            string? line = reader.ReadLine();
                            if (line == null) throw new IOException("Нет ответа от сервера");
                            var msg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                            if (msg == null || !msg.ContainsKey("command"))
                            {
                                this.Invoke(new Action(() => lblAgentStatus.Text = "Статус: Некорректный ответ сервера"));
                                Thread.Sleep(5000);
                                continue;
                            }
                            string cmd = msg["command"].ToString();
                            if (cmd == "NO_TASK")
                            {
                                this.Invoke(new Action(() => lblAgentStatus.Text = "Статус: Нет заданий, жду..."));
                                Thread.Sleep(10000);
                                continue;
                            }
                            if (cmd == "TASK")
                            {
                                int blockId = Convert.ToInt32(msg["blockId"]);
                                long startIndex = Convert.ToInt64(msg["startIndex"]);
                                long endIndex = Convert.ToInt64(msg["endIndex"]);
                                int wordCount = Convert.ToInt32(msg["wordCount"]);
                                string address = msg.ContainsKey("address") ? msg["address"].ToString() : "";
                                string seed = msg.ContainsKey("seed") ? msg["seed"].ToString() : "";
                                this.Invoke(new Action(() => lblAgentStatus.Text = $"Статус: Работаю над блоком {blockId} ({startIndex}-{endIndex})"));

                                string progressFile = "agent_progress.json";
                                if (File.Exists(progressFile))
                                {
                                    try
                                    {
                                        var json = File.ReadAllText(progressFile);
                                        var prog = JsonSerializer.Deserialize<AgentProgress>(json);
                                        if (prog != null && prog.blockId == blockId && prog.currentIndex >= startIndex && prog.currentIndex <= endIndex)
                                        {
                                            startIndex = prog.currentIndex;
                                            this.Invoke(new Action(() => lblAgentStatus.Text = $"Статус: Возобновление блока {blockId} с позиции {startIndex}"));
                                        }
                                    }
                                    catch { }
                                }

                                int reportInterval = agentClient.ProgressReportInterval; // из конфигурации
                                for (long i = startIndex; i <= endIndex && !token.IsCancellationRequested; i++)
                                {
                                    // Генерируем seed-фразу по индексу
                                    var possibleWords = new List<string>[wordCount];
                                    for (int w = 0; w < wordCount; w++) possibleWords[w] = finder.GetBip39Words();
                                    var combination = finder.GenerateCombinationByIndex(new System.Numerics.BigInteger(i), possibleWords);
                                    var seedPhrase = string.Join(" ", combination);
                                    string wif = null;
                                    try {
                                        var mnemonic = new NBitcoin.Mnemonic(seedPhrase, NBitcoin.Wordlist.English);
                                        var seedBytes = mnemonic.DeriveSeed();
                                        var masterKey = NBitcoin.ExtKey.CreateFromSeed(seedBytes);
                                        var fullPath = new NBitcoin.KeyPath("44'/0'/0'/0/0");
                                        var privateKey = masterKey.Derive(fullPath).PrivateKey;
                                        wif = privateKey.GetWif(NBitcoin.Network.Main).ToString();
                                    } catch { wif = null; }
                                    // Обновляем верхний label приватным ключом
                                    if (!string.IsNullOrEmpty(wif))
                                        this.Invoke(new Action(() => lblCurrentPhrase.Text = $"Приватный ключ: {wif}"));
                                    // Проверяем валидность seed-фразы
                                    if (finder.IsValidSeedPhrase(seedPhrase))
                                    {
                                        var generatedAddress = finder.GenerateBitcoinAddress(seedPhrase);
                                        if (!string.IsNullOrEmpty(address) && generatedAddress == address)
                                        {
                                            // Сообщить о найденной фразе
                                            writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { command = "REPORT_FOUND", blockId = blockId, combination = seedPhrase }));
                                            string? ackFound = reader.ReadLine();
                                            this.Invoke(new Action(() => txtResults.AppendText($"НАЙДЕНО: {seedPhrase}\r\n")));
                                        }
                                    }
                                    // Отправлять прогресс каждые reportInterval
                                    if (i % reportInterval == 0)
                                    {
                                        writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { command = "REPORT_PROGRESS", blockId = blockId, currentIndex = i }));
                                        string? ack = reader.ReadLine();
                                        try
                                        {
                                            var prog = new AgentProgress { blockId = blockId, currentIndex = i };
                                            File.WriteAllText(progressFile, System.Text.Json.JsonSerializer.Serialize(prog));
                                        }
                                        catch { }
                                    }
                                }
                                // После завершения блока — сообщить о завершении
                                writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { command = "RELEASE_BLOCK", blockId = blockId }));
                                string? ack2 = reader.ReadLine();
                                try { File.Delete(progressFile); } catch { }
                                this.Invoke(new Action(() => lblAgentStatus.Text = $"Статус: Блок {blockId} завершён, жду новое задание..."));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() => {
                    isAgentConnected = false;
                    lblAgentStatus.Text = $"Ошибка: {ex.Message}";
                    btnAgentConnect.Text = "Подключиться";
                }));
            }
        }
    }

    public class SearchParameters
    {
        public string SeedPhrase { get; set; } = "";
        public string BitcoinAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public bool FullSearch { get; set; } = false;
        public int ThreadCount { get; set; } = 1;
        public string? ProgressFile { get; set; } = null;
    }

    public class ProgressData
    {
        public string SeedPhrase { get; set; } = "";
        public string BitcoinAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public bool FullSearch { get; set; } = false;
        public int ThreadCount { get; set; } = 1;
        public DateTime Timestamp { get; set; }
        public string CurrentCombination { get; set; } = "0";
        public string TotalCombinations { get; set; } = "0";
        public string LastCheckedPhrase { get; set; } = "";
    }

    public class ProgressInfo
    {
        public BigInteger Current { get; set; }
        public BigInteger Total { get; set; }
        public double Percentage { get; set; }
        public string Status { get; set; } = "";
        public double Rate { get; set; }
        public TimeSpan Remaining { get; set; }
        public List<string> CurrentPhrases { get; set; } = new List<string>();
        public string? CurrentPrivateKey { get; set; } // новый параметр
    }

    public class AgentProgress
    {
        public int blockId { get; set; }
        public long currentIndex { get; set; }
    }
}
