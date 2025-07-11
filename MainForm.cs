using System;
using System.Drawing;
using System.Windows.Forms;

namespace BitcoinFinder
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(520, 420);
            this.Text = "Bitcoin Finder - Выбор режима";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.WhiteSmoke;

            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 4;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F)); // Заголовок
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // Отступ
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210F)); // Кнопки
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Инфо
            mainLayout.Padding = new Padding(20, 20, 20, 20);

            // Заголовок
            var lblTitle = new Label();
            lblTitle.Text = "Bitcoin Seed Phrase Finder";
            lblTitle.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.Dock = DockStyle.Fill;
            mainLayout.Controls.Add(lblTitle, 0, 0);

            // Отступ
            var spacer = new Panel { Height = 10, Dock = DockStyle.Fill };
            mainLayout.Controls.Add(spacer, 0, 1);

            // Кнопки выбора режима
            var buttonPanel = new TableLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.ColumnCount = 1;
            buttonPanel.RowCount = 3;
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
            buttonPanel.Padding = new Padding(10, 0, 10, 0);
            buttonPanel.BackColor = Color.White;

            // Кнопка автономного режима
            var btnStandalone = new Button();
            btnStandalone.Text = "🔍  Автономный поиск";
            btnStandalone.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            btnStandalone.BackColor = Color.FromArgb(220, 255, 220);
            btnStandalone.ForeColor = Color.DarkGreen;
            btnStandalone.Height = 50;
            btnStandalone.Dock = DockStyle.Top;
            btnStandalone.Margin = new Padding(0, 10, 0, 10);
            btnStandalone.FlatStyle = FlatStyle.Flat;
            btnStandalone.FlatAppearance.BorderSize = 0;
            btnStandalone.Cursor = Cursors.Hand;
            btnStandalone.Click += BtnStandalone_Click;
            buttonPanel.Controls.Add(btnStandalone, 0, 0);

            // Кнопка агентского режима
            var btnAgent = new Button();
            btnAgent.Text = "🤖  Режим агента";
            btnAgent.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            btnAgent.BackColor = Color.FromArgb(220, 240, 255);
            btnAgent.ForeColor = Color.DarkBlue;
            btnAgent.Height = 50;
            btnAgent.Dock = DockStyle.Top;
            btnAgent.Margin = new Padding(0, 10, 0, 10);
            btnAgent.FlatStyle = FlatStyle.Flat;
            btnAgent.FlatAppearance.BorderSize = 0;
            btnAgent.Cursor = Cursors.Hand;
            btnAgent.Click += BtnAgent_Click;
            buttonPanel.Controls.Add(btnAgent, 0, 1);

            // Кнопка серверного режима
            var btnServer = new Button();
            btnServer.Text = "🖥️  Сервер координатор";
            btnServer.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            btnServer.BackColor = Color.FromArgb(255, 245, 200);
            btnServer.ForeColor = Color.DarkOrange;
            btnServer.Height = 50;
            btnServer.Dock = DockStyle.Top;
            btnServer.Margin = new Padding(0, 10, 0, 10);
            btnServer.FlatStyle = FlatStyle.Flat;
            btnServer.FlatAppearance.BorderSize = 0;
            btnServer.Cursor = Cursors.Hand;
            btnServer.Click += BtnServer_Click;
            buttonPanel.Controls.Add(btnServer, 0, 2);

            mainLayout.Controls.Add(buttonPanel, 0, 2);

            // Информация
            var lblInfo = new Label();
            lblInfo.Text = "Выберите режим работы:\n\n" +
                          "• Автономный поиск — для локального поиска seed-фраз\n" +
                          "• Режим агента — для подключения к серверу координатору\n" +
                          "• Сервер координатор — для управления распределёнными агентами";
            lblInfo.Font = new Font("Segoe UI", 11F);
            lblInfo.TextAlign = ContentAlignment.TopCenter;
            lblInfo.Dock = DockStyle.Fill;
            mainLayout.Controls.Add(lblInfo, 0, 3);

            this.Controls.Add(mainLayout);
        }

        private void BtnStandalone_Click(object? sender, EventArgs e)
        {
            var standaloneForm = new StandaloneForm();
            standaloneForm.Show();
        }

        private void BtnAgent_Click(object? sender, EventArgs e)
        {
            var agentForm = new AgentForm();
            agentForm.Show();
        }

        private void BtnServer_Click(object? sender, EventArgs e)
        {
            var serverForm = new ServerForm();
            serverForm.Show();
        }
    }
} 