using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BitcoinFinder
{
    public class ServerMonitorForm : Form
    {
        private DataGridView dgvBlocks;
        private DataGridView dgvAgents;
        private ListBox lstResults;
        private Button btnResetStale;
        private Timer updateTimer;
        private DistributedMasterServer server;
        private Label lblBlocks;
        private Label lblAgents;
        private Label lblResults;

        public ServerMonitorForm(DistributedMasterServer serverInstance)
        {
            this.server = serverInstance;
            this.Text = "Монитор сервера (мастер)";
            this.Size = new Size(1200, 700);
            this.Font = new Font("Segoe UI", 12F);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            lblBlocks = new Label { Text = "Очередь блоков", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
            lblAgents = new Label { Text = "Список агентов", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
            lblResults = new Label { Text = "Найденные результаты", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 14F, FontStyle.Bold) };

            dgvBlocks = new DataGridView { Dock = DockStyle.Top, Height = 220, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, Font = new Font("Segoe UI", 11F) };
            dgvBlocks.Columns.Add("BlockId", "ID блока");
            dgvBlocks.Columns.Add("StartIndex", "Начало");
            dgvBlocks.Columns.Add("EndIndex", "Конец");
            dgvBlocks.Columns.Add("Status", "Статус");
            dgvBlocks.Columns.Add("AssignedAgent", "Агент");
            dgvBlocks.Columns.Add("Progress", "Прогресс");
            dgvBlocks.Columns.Add("LastUpdate", "Последнее обновление");

            dgvAgents = new DataGridView { Dock = DockStyle.Top, Height = 150, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, Font = new Font("Segoe UI", 11F) };
            dgvAgents.Columns.Add("AgentId", "ID агента");
            dgvAgents.Columns.Add("IP", "IP адрес");
            dgvAgents.Columns.Add("AssignedBlock", "Назначенный блок");
            dgvAgents.Columns.Add("LastReport", "Последний отчёт");

            lstResults = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 11F) };

            btnResetStale = new Button { Text = "Сбросить зависшие блоки", Dock = DockStyle.Bottom, Height = 40, Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            btnResetStale.Click += (s, e) => {
                server.ReassignStaleBlocks(TimeSpan.FromMinutes(2));
                UpdateData();
            };

            var mainPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1 };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // lblBlocks
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 220)); // dgvBlocks
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // lblAgents
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); // dgvAgents
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // lblResults
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // lstResults
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // btnResetStale
            mainPanel.Controls.Add(lblBlocks, 0, 0);
            mainPanel.Controls.Add(dgvBlocks, 0, 1);
            mainPanel.Controls.Add(lblAgents, 0, 2);
            mainPanel.Controls.Add(dgvAgents, 0, 3);
            mainPanel.Controls.Add(lblResults, 0, 4);
            mainPanel.Controls.Add(lstResults, 0, 5);
            mainPanel.Controls.Add(btnResetStale, 0, 6);
            this.Controls.Add(mainPanel);

            updateTimer = new Timer();
            updateTimer.Interval = 2000; // 2 секунды
            updateTimer.Tick += (s, e) => UpdateData();
            updateTimer.Start();
            UpdateData();
        }

        private void UpdateData()
        {
            // Очередь блоков
            dgvBlocks.Rows.Clear();
            foreach (var block in server.BlockQueue)
            {
                int rowIdx = dgvBlocks.Rows.Add(block.BlockId, block.StartIndex, block.EndIndex, block.Status, block.AssignedAgent, block.Progress, block.LastUpdate.ToString("HH:mm:ss"));
                if (block.Status == "assigned" && (DateTime.Now - block.LastUpdate).TotalMinutes > 2)
                {
                    dgvBlocks.Rows[rowIdx].DefaultCellStyle.BackColor = Color.LightPink;
                }
                else if (block.Status == "done")
                {
                    dgvBlocks.Rows[rowIdx].DefaultCellStyle.BackColor = Color.LightGreen;
                }
            }
            // Агенты
            dgvAgents.Rows.Clear();
            foreach (var agent in server.AgentList)
            {
                dgvAgents.Rows.Add(agent.AgentId, agent.IP, agent.AssignedBlock, agent.LastReport.ToString("HH:mm:ss"));
            }
            // Результаты
            lstResults.Items.Clear();
            foreach (var res in server.FoundResults)
                lstResults.Items.Add(res);
        }
    }
} 