using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using SocialNetworkArmy.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SocialNetworkArmy.Forms
{
    public partial class MainForm : Form
    {
        private List<Profile> profiles;
        private readonly ProfileService profileService;
        private readonly FingerprintService fingerprintService;
        private ScheduleService scheduleService;
        private Button scheduleButton;
        private DataGridView profilesGridView;
        private ComboBox platformComboBox;
        private TextBox profileNameTextBox;
        private TextBox proxyTextBox;
        private TextBox groupNameTextBox;  // ✅ AJOUT
        private Label statusLabel;
        private TextBox scheduleLogTextBox;
        private ComboBox dataFilesComboBox;
        private Panel logPanel;
        private ToolTip modernToolTip;
        private Point hoveredCell = new Point(-1, -1);
        private Font Sergoe = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public MainForm()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                  ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
            InitializeComponent();
            if (Environment.OSVersion.Version.Major >= 10)
            {
                int useImmersiveDarkMode = 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
            }
            this.Icon = new System.Drawing.Icon("Data\\Icons\\MainForm.ico");
            profileService = new ProfileService();
            fingerprintService = new FingerprintService();
            profiles = profileService.LoadProfiles();
            this.Resize += MainForm_Resize;
            scheduleService = new ScheduleService(scheduleLogTextBox, profileService);

            PopulateProfilesList();
            LoadDataFiles();
        }

        private void InitializeComponent()
        {
            this.ForeColor = Color.White;
            this.Font = Sergoe;
            this.AutoScaleDimensions = new SizeF(8F, 20F);
            this.AutoScaleMode = AutoScaleMode.Font;

            // Modern ToolTip
            modernToolTip = new ToolTip
            {
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                OwnerDraw = true,
                IsBalloon = false
            };
            modernToolTip.Draw += ModernToolTip_Draw;

            // ProfilesGridView with modern styling
            profilesGridView = new TransparentDataGridView
            {
                Location = new Point(12, 12),
                Size = new Size(960, 200),
                BackgroundColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.White,
                Font = Sergoe,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(45, 45, 45),
                ColumnHeadersHeight = 38,
                EnableHeadersVisualStyles = false,
                AllowUserToResizeRows = false,
                RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            };

            profilesGridView.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(220, 20, 20, 20),
                ForeColor = Color.FromArgb(180, 180, 180),
                Padding = new Padding(8, 6, 8, 6),
                Font = Sergoe,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                SelectionBackColor = Color.FromArgb(220, 20, 20, 20),
                SelectionForeColor = Color.FromArgb(180, 180, 180)
            };

            profilesGridView.RowTemplate.Height = 36;

            profilesGridView.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(220, 24, 24, 24),
                ForeColor = Color.White,
                SelectionBackColor = Color.FromArgb(33, 150, 243),
                SelectionForeColor = Color.White,
                Padding = new Padding(8, 4, 8, 4)
            };

            profilesGridView.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(220, 28, 28, 28),
                ForeColor = Color.White,
                SelectionBackColor = Color.FromArgb(33, 150, 243),
                SelectionForeColor = Color.White,
                Padding = new Padding(8, 4, 8, 4)
            };

            profilesGridView.CellContentClick += ProfilesGridView_CellContentClick;
            profilesGridView.CellPainting += ProfilesGridView_CellPainting;
            profilesGridView.CellMouseEnter += ProfilesGridView_CellMouseEnter;
            profilesGridView.CellMouseLeave += ProfilesGridView_CellMouseLeave;

            // Colonnes
            profilesGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "Name",
                Width = 130,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });

            profilesGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Platform",
                HeaderText = "Platform",
                Width = 90,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });

            profilesGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Group",
                HeaderText = "Group",
                Width = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });

            profilesGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Proxy",
                HeaderText = "Proxy",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            });

            profilesGridView.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "Edit",
                HeaderText = "Edit",
                Text = "✏️ Edit",
                UseColumnTextForButtonValue = true,
                Width = 90,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });

            profilesGridView.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "Config",
                HeaderText = "Config",
                Text = "⚙️ Config",
                UseColumnTextForButtonValue = true,
                Width = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });

            profilesGridView.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "Delete",
                HeaderText = "Delete",
                Text = "🗑️ Delete",
                UseColumnTextForButtonValue = true,
                Width = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });

            // Platform ComboBox
            platformComboBox = new ComboBox
            {
                Location = new Point(12, 225),
                Size = new Size(120, 34),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                Font = Sergoe,
                FlatStyle = FlatStyle.Flat
            };
            platformComboBox.Items.AddRange(new object[] { "Instagram", "TikTok" });
            modernToolTip.SetToolTip(platformComboBox, "Select platform");

            // Profile Name TextBox
            profileNameTextBox = new TextBox
            {
                Location = new Point(140, 225),
                Size = new Size(120, 34),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.Gray,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Sergoe,
                Text = "Name"
            };
            profileNameTextBox.GotFocus += (s, e) =>
            {
                if (profileNameTextBox.Text == "Name")
                {
                    profileNameTextBox.Text = "";
                    profileNameTextBox.ForeColor = Color.White;
                }
                profileNameTextBox.BackColor = Color.FromArgb(42, 42, 42);
            };
            profileNameTextBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(profileNameTextBox.Text))
                {
                    profileNameTextBox.Text = "Name";
                    profileNameTextBox.ForeColor = Color.Gray;
                }
                profileNameTextBox.BackColor = Color.FromArgb(35, 35, 35);
            };
            modernToolTip.SetToolTip(profileNameTextBox, "Enter profile name (3-20 characters)");

            // Proxy TextBox
            proxyTextBox = new TextBox
            {
                Location = new Point(270, 225),
                Size = new Size(200, 34),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.Gray,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Sergoe,
                Text = "Proxy (optional)"
            };
            proxyTextBox.GotFocus += (s, e) =>
            {
                if (proxyTextBox.Text == "Proxy (optional)")
                {
                    proxyTextBox.Text = "";
                    proxyTextBox.ForeColor = Color.White;
                }
                proxyTextBox.BackColor = Color.FromArgb(42, 42, 42);
            };
            proxyTextBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(proxyTextBox.Text))
                {
                    proxyTextBox.Text = "Proxy (optional)";
                    proxyTextBox.ForeColor = Color.Gray;
                }
                proxyTextBox.BackColor = Color.FromArgb(35, 35, 35);
            };
            modernToolTip.SetToolTip(proxyTextBox, "Format: IP:port or http://user:pass@IP:port");

            // ✅ Group Name TextBox
            groupNameTextBox = new TextBox
            {
                Location = new Point(478, 225),
                Size = new Size(120, 34),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.Gray,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Sergoe,
                Text = "Group (optional)"
            };
            groupNameTextBox.GotFocus += (s, e) =>
            {
                if (groupNameTextBox.Text == "Group (optional)")
                {
                    groupNameTextBox.Text = "";
                    groupNameTextBox.ForeColor = Color.White;
                }
                groupNameTextBox.BackColor = Color.FromArgb(42, 42, 42);
            };
            groupNameTextBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(groupNameTextBox.Text))
                {
                    groupNameTextBox.Text = "Group (optional)";
                    groupNameTextBox.ForeColor = Color.Gray;
                }
                groupNameTextBox.BackColor = Color.FromArgb(35, 35, 35);
            };
            modernToolTip.SetToolTip(groupNameTextBox, "Group name for scheduler");

            // Create Button
            Button createButton = new Button
            {
                Text = "➕ Create",
                Location = new Point(606, 220),
                Size = new Size(100, 34),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                Font = Sergoe,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            createButton.FlatAppearance.BorderSize = 0;
            createButton.Click += CreateButton_Click;
            modernToolTip.SetToolTip(createButton, "Create new profile");

            // Vertical Separator
            Panel separator = new Panel
            {
                Location = new Point(713, 222),
                Size = new Size(2, 28),
                BackColor = Color.FromArgb(50, 50, 50)
            };

            // Data Files Section
            Label dataLabel = new Label
            {
                Text = "📁 Files:",
                Location = new Point(723, 220),
                Size = new Size(70, 34),
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = Sergoe,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            dataFilesComboBox = new ComboBox
            {
                Location = new Point(796, 225),
                Size = new Size(96, 34),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                Font = Sergoe,
                DropDownWidth = 300,
                FlatStyle = FlatStyle.Flat
            };
            modernToolTip.SetToolTip(dataFilesComboBox, "Select data file to open");

            Button openDataFileButton = new Button
            {
                Text = "📂",
                Location = new Point(897, 220),
                Size = new Size(75, 34),
                BackColor = Color.FromArgb(96, 125, 139),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Emoji", 11f),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            openDataFileButton.FlatAppearance.BorderSize = 0;
            openDataFileButton.Click += OpenDataFileButton_Click;
            modernToolTip.SetToolTip(openDataFileButton, "Open selected file");

            // Buttons Row
            int buttonY = 262;
            int buttonSpacing = 165;

            Button launchButton = new Button
            {
                Text = "▶️ Open Profile",
                Location = new Point(12, buttonY),
                Size = new Size(155, 45),
                BackColor = Color.FromArgb(255, 152, 0),
                ForeColor = Color.White,
                Font = Sergoe,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            launchButton.FlatAppearance.BorderSize = 0;
            launchButton.Click += LaunchButton_Click;
            modernToolTip.SetToolTip(launchButton, "Launch selected profile bot");

            Button openPhone = new Button
            {
                Text = "📱 Open Phone",
                Location = new Point(12 + buttonSpacing, buttonY),
                Size = new Size(155, 45),
                BackColor = Color.FromArgb(156, 39, 176),
                ForeColor = Color.White,
                Font = Sergoe,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            openPhone.FlatAppearance.BorderSize = 0;
            openPhone.Click += StoryButton_Click;
            modernToolTip.SetToolTip(openPhone, "Open story poster for mobile");

            scheduleButton = new Button
            {
                Text = "⏰ Start Schedule",
                Location = new Point(12 + buttonSpacing * 2, buttonY),
                Size = new Size(155, 45),
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                Font = Sergoe,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            scheduleButton.FlatAppearance.BorderSize = 0;
            scheduleButton.Click += ScheduleButton_Click;
            modernToolTip.SetToolTip(scheduleButton, "Start automatic scheduler");

            statusLabel = new Label
            {
                Location = new Point(687, 268),
                Size = new Size(285, 34),
                Text = "Ready",
                AutoSize = false,
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            logPanel = new Panel
            {
                Location = new Point(12, 315),
                Size = new Size(960, 175),
                BackColor = Color.Transparent
            };

            scheduleLogTextBox = new TextBox
            {
                Location = new Point(1, 1),
                Size = new Size(958, 173),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8f, FontStyle.Regular),
                Text = "[Schedule] Ready. Click 'Start Schedule' to begin.\r\n"
            };

            // Form settings
            this.ClientSize = new Size(984, 502);
            this.MinimumSize = new Size(984, 502);
            this.Text = "SocialNetworkArmy";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            // Add controls
            this.Controls.Add(profilesGridView);
            this.Controls.Add(platformComboBox);
            this.Controls.Add(profileNameTextBox);
            this.Controls.Add(proxyTextBox);
            this.Controls.Add(groupNameTextBox);  // ✅ AJOUT
            this.Controls.Add(createButton);
            this.Controls.Add(separator);
            this.Controls.Add(dataLabel);
            this.Controls.Add(dataFilesComboBox);
            this.Controls.Add(openDataFileButton);
            this.Controls.Add(launchButton);
            this.Controls.Add(scheduleButton);
            this.Controls.Add(openPhone);
            this.Controls.Add(statusLabel);
            this.Controls.Add(logPanel);
            logPanel.Controls.Add(scheduleLogTextBox);

            this.FormClosing += MainForm_FormClosing;
        }

        private void ProfilesGridView_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (e.ColumnIndex == profilesGridView.Columns["Edit"].Index ||
                e.ColumnIndex == profilesGridView.Columns["Config"].Index ||
                e.ColumnIndex == profilesGridView.Columns["Delete"].Index)
            {
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(200, 28, 28, 28)), e.CellBounds);

                Color buttonColor;
                string buttonText;

                if (e.ColumnIndex == profilesGridView.Columns["Edit"].Index)
                {
                    buttonColor = Color.FromArgb(33, 150, 243); // Blue
                    buttonText = "✏️ Edit";
                }
                else if (e.ColumnIndex == profilesGridView.Columns["Config"].Index)
                {
                    buttonColor = Color.FromArgb(255, 152, 0); // Orange
                    buttonText = "⚙️ Config";
                }
                else // Delete
                {
                    buttonColor = Color.FromArgb(244, 67, 54); // Red
                    buttonText = "🗑️ Delete";
                }

                bool isHovered = hoveredCell.X == e.ColumnIndex && hoveredCell.Y == e.RowIndex;
                if (isHovered)
                {
                    buttonColor = DarkenColor(buttonColor, 30);
                }

                Rectangle buttonRect = new Rectangle(
                    e.CellBounds.X + 8,
                    e.CellBounds.Y + 6,
                    e.CellBounds.Width - 16,
                    e.CellBounds.Height - 12
                );

                e.Graphics.FillRectangle(new SolidBrush(buttonColor), buttonRect);
                TextRenderer.DrawText(
                    e.Graphics,
                    buttonText,
                    Sergoe,
                    buttonRect,
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );

                e.Handled = true;
            }
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
                return;

            int formWidth = this.ClientSize.Width;
            int formHeight = this.ClientSize.Height;

            profilesGridView.Width = formWidth - 24;
            profilesGridView.Height = Math.Max(200, formHeight - 465);

            int controlsY = profilesGridView.Bottom + 13;
            platformComboBox.Top = controlsY;
            profileNameTextBox.Top = controlsY;
            proxyTextBox.Top = controlsY;
            groupNameTextBox.Top = controlsY;  // ✅ AJOUT

            int filesX = formWidth - 224;
            dataFilesComboBox.Left = filesX;
            dataFilesComboBox.Top = controlsY;

            int openButtonX = formWidth - 87;

            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Button btn)
                {
                    if (btn.Text == "📂")
                    {
                        btn.Left = openButtonX;
                        btn.Top = controlsY - 5;
                    }
                    else if (btn.Text == "➕ Create")
                    {
                        btn.Top = controlsY - 5;
                    }
                }
                else if (ctrl is Label lbl && lbl.Text == "📁 Files:")
                {
                    lbl.Left = filesX - 73;
                    lbl.Top = controlsY - 5;
                }
                else if (ctrl is Panel panel && panel.Size.Width == 2)
                {
                    panel.Left = filesX - 83;
                    panel.Top = controlsY - 3;
                }
            }

            int buttonsY = controlsY + 42;
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Button btn)
                {
                    if (btn.Text.Contains("Open Profile") || btn.Text.Contains("Open Phone") || btn.Text.Contains("Schedule"))
                    {
                        btn.Top = buttonsY;
                    }
                }
            }

            statusLabel.Top = buttonsY + 6;
            statusLabel.Left = formWidth - 297;
            statusLabel.Width = 285;

            logPanel.Top = buttonsY + 53;
            logPanel.Width = formWidth - 24;

            int availableHeight = formHeight - logPanel.Top - 12;
            logPanel.Height = Math.Max(100, Math.Min(350, availableHeight));

            scheduleLogTextBox.Width = logPanel.Width - 2;
            scheduleLogTextBox.Height = logPanel.Height - 2;

            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            float centerX = this.ClientRectangle.Width / 2f;
            float centerY = this.ClientRectangle.Height / 2f;
            float angle = 80f;
            float distance = 600f;

            double radians = angle * Math.PI / 180.0;
            PointF point1 = new PointF(
                centerX - (float)(Math.Cos(radians) * distance),
                centerY - (float)(Math.Sin(radians) * distance)
            );
            PointF point2 = new PointF(
                centerX + (float)(Math.Cos(radians) * distance),
                centerY + (float)(Math.Sin(radians) * distance)
            );

            using (LinearGradientBrush brush = new LinearGradientBrush(
                point1,
                point2,
                Color.FromArgb(15, 15, 15),
                Color.FromArgb(15, 15, 15)
            ))
            {
                ColorBlend colorBlend = new ColorBlend();
                colorBlend.Colors = new Color[]
                {
                    Color.FromArgb(0, 0, 0),
                    Color.FromArgb(15, 15, 15),
                    Color.FromArgb(50, 50, 50),
                    Color.FromArgb(15, 15, 15),
                    Color.FromArgb(0, 0, 0)
                };
                colorBlend.Positions = new float[]
                {
                    0.0f,
                    0.30f,
                    0.5f,
                    0.70f,
                    1.0f
                };

                brush.InterpolationColors = colorBlend;
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }

            DrawControlShadow(e.Graphics, profilesGridView);

            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Panel panel && panel.Controls.Contains(scheduleLogTextBox))
                {
                    DrawControlShadow(e.Graphics, panel);
                    break;
                }
            }

            base.OnPaint(e);
        }

        private void DrawControlShadow(Graphics g, Control control)
        {
            if (control == null) return;

            Rectangle shadowRect = new Rectangle(
                control.Left + 2,
                control.Top + 2,
                control.Width,
                control.Height
            );

            using (GraphicsPath shadowPath = GetRoundedRect(shadowRect, 3))
            {
                using (PathGradientBrush shadowBrush = new PathGradientBrush(shadowPath))
                {
                    shadowBrush.CenterColor = Color.FromArgb(80, 0, 0, 0);
                    shadowBrush.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                    shadowBrush.FocusScales = new PointF(0.85f, 0.85f);

                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(shadowBrush, shadowPath);
                }
            }
        }

        private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        private void ModernToolTip_Draw(object sender, DrawToolTipEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GetRoundedRect(e.Bounds, 6))
            {
                e.Graphics.FillPath(new SolidBrush(Color.FromArgb(40, 40, 40)), path);
                e.Graphics.DrawPath(new Pen(Color.FromArgb(60, 60, 60), 1), path);
            }
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (scheduleService != null && scheduleService.IsRunning)
            {
                scheduleLogTextBox.AppendText("[Schedule] Shutting down scheduler...\r\n");
                await scheduleService.ToggleAsync();
            }
        }

        private void PopulateProfilesList()
        {
            profilesGridView.Rows.Clear();
            foreach (var profile in profiles)
            {
                profilesGridView.Rows.Add(
                    profile.Name,
                    profile.Platform,
                    string.IsNullOrEmpty(profile.GroupName) ? "Solo" : profile.GroupName,
                    profile.Proxy ?? "Local",
                    "✏️ Edit",
                    "⚙️ Config",
                    "🗑️ Delete"
                );
            }
        }

        private void ProfilesGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            string profileName = profilesGridView.Rows[e.RowIndex].Cells["Name"].Value.ToString();
            var profile = profiles.FirstOrDefault(p => p.Name == profileName);

            if (profile == null) return;

            if (e.ColumnIndex == profilesGridView.Columns["Edit"].Index)
            {
                using (var editForm = new EditProfileForm(profile))
                {
                    if (editForm.ShowDialog() == DialogResult.OK)
                    {
                        string newName = editForm.ProfileName;
                        string newProxy = editForm.ProxyValue;
                        string newGroupName = editForm.GroupNameValue;

                        if (!IsValidProfileName(newName))
                        {
                            MessageBox.Show("Invalid name! Use letters, numbers, dashes or underscores only (3-20 characters).");
                            return;
                        }

                        if (newName != profile.Name && profiles.Any(p => p.Name == newName))
                        {
                            MessageBox.Show("This profile name already exists!");
                            return;
                        }

                        // ✅ VÉRIFIER QUE LE NOUVEAU NOM N'EST PAS UN GROUPE EXISTANT
                        if (newName != profile.Name && profiles.Any(p => !string.IsNullOrWhiteSpace(p.GroupName) && p.GroupName.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                        {
                            MessageBox.Show($"Cannot use '{newName}' as profile name: it's already used as a group name!");
                            return;
                        }

                        // ✅ VÉRIFIER QUE LE NOUVEAU GROUPE N'EST PAS UN NOM DE PROFIL EXISTANT
                        if (!string.IsNullOrWhiteSpace(newGroupName) && profiles.Any(p => p.Name.Equals(newGroupName, StringComparison.OrdinalIgnoreCase)))
                        {
                            MessageBox.Show($"Cannot use '{newGroupName}' as group name: a profile with this name already exists!");
                            return;
                        }

                        if (!string.IsNullOrEmpty(newProxy) && !IsValidProxy(newProxy))
                        {
                            MessageBox.Show("Invalid proxy format! Ex: IP:port or http://user:pass@IP:port");
                            return;
                        }

                        if (!string.IsNullOrEmpty(newProxy))
                        {
                            var proxyService = new ProxyService();
                            if (!proxyService.ValidateProxyWhitelist(newProxy, out string errorMessage))
                            {
                                MessageBox.Show(errorMessage, "Proxy Not Authorized", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }
                        }

                        profile.Name = newName;
                        profile.Proxy = string.IsNullOrEmpty(newProxy) ? "" : newProxy;
                        profile.GroupName = string.IsNullOrEmpty(newGroupName) ? "" : newGroupName;

                        profileService.SaveProfiles(profiles);
                        PopulateProfilesList();
                        statusLabel.Text = $"✅ Profile '{newName}' updated!";
                    }
                }
            }
            else if (e.ColumnIndex == profilesGridView.Columns["Config"].Index)
            {
                using (var configForm = new ConfigForm(profile.Name))
                {
                    if (configForm.ShowDialog() == DialogResult.OK)
                    {
                        statusLabel.Text = $"✅ Configuration saved for '{profile.Name}'";
                    }
                }
            }
            else if (e.ColumnIndex == profilesGridView.Columns["Delete"].Index)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete profile '{profile.Name}'?",
                    "Confirm Delete",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    profiles.Remove(profile);
                    profileService.SaveProfiles(profiles);
                    PopulateProfilesList();
                    statusLabel.Text = $"🗑️ Profile '{profile.Name}' deleted!";
                }
            }
        }

        private void CreateButton_Click(object sender, EventArgs e)
        {
            string name = profileNameTextBox.Text == "Name" ? "" : profileNameTextBox.Text.Trim();
            string groupName = (groupNameTextBox.Text == "Group (optional)" || string.IsNullOrWhiteSpace(groupNameTextBox.Text))
                ? ""
                : groupNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) || platformComboBox.SelectedItem == null)
            {
                MessageBox.Show("Fill in name and platform!");
                return;
            }

            if (!IsValidProfileName(name))
            {
                MessageBox.Show("Invalid name! Use letters, numbers, dashes or underscores only (3-20 characters).");
                return;
            }

            string platform = platformComboBox.SelectedItem.ToString();
            string proxy = (proxyTextBox.Text == "Proxy (optional)" || string.IsNullOrWhiteSpace(proxyTextBox.Text))
                ? ""
                : proxyTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(proxy) && !IsValidProxy(proxy))
            {
                MessageBox.Show("Invalid proxy format! Ex: IP:port or http://user:pass@IP:port");
                return;
            }

            if (!string.IsNullOrEmpty(proxy))
            {
                var proxyService = new ProxyService();
                if (!proxyService.ValidateProxyWhitelist(proxy, out string errorMessage))
                {
                    MessageBox.Show(errorMessage, "Proxy Not Authorized", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    proxyTextBox.Focus();
                    proxyTextBox.SelectAll();
                    return;
                }
            }

            if (profiles.Any(p => p.Name == name))
            {
                MessageBox.Show("This profile name already exists!");
                return;
            }

            // ✅ VÉRIFIER QUE LE NOM N'EST PAS UN GROUPE EXISTANT
            if (!string.IsNullOrWhiteSpace(name) && profiles.Any(p => !string.IsNullOrWhiteSpace(p.GroupName) && p.GroupName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Cannot use '{name}' as profile name: it's already used as a group name!");
                return;
            }

            // ✅ VÉRIFIER QUE LE GROUPE N'EST PAS UN NOM DE PROFIL EXISTANT
            if (!string.IsNullOrWhiteSpace(groupName) && profiles.Any(p => p.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Cannot use '{groupName}' as group name: a profile with this name already exists!");
                return;
            }

            var fingerprint = fingerprintService.GenerateDesktopFingerprint();
            var serializedFingerprint = JsonConvert.SerializeObject(fingerprint);

            var newProfile = new Profile
            {
                Name = name,
                Platform = platform,
                Proxy = proxy,
                GroupName = groupName,
                Fingerprint = serializedFingerprint,
                StorageState = ""
            };

            profiles.Add(newProfile);
            profileService.SaveProfiles(profiles);
            PopulateProfilesList();
            statusLabel.Text = $"✅ Profile '{name}' created!";

            profileNameTextBox.Text = "Name";
            profileNameTextBox.ForeColor = Color.Gray;
            proxyTextBox.Text = "Proxy (optional)";
            proxyTextBox.ForeColor = Color.Gray;
            groupNameTextBox.Text = "Group (optional)";
            groupNameTextBox.ForeColor = Color.Gray;
        }

        private bool IsValidProfileName(string name)
        {
            var regex = new Regex(@"^[a-zA-Z0-9_.-]{3,20}$");
            return regex.IsMatch(name);
        }

        private bool IsValidProxy(string proxy)
        {
            var regex = new Regex(@"^(http://)?(([^:]+):([^@]+)@)?([\w\.-]+|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{1,5})$", RegexOptions.IgnoreCase);
            return regex.IsMatch(proxy);
        }

        private void LaunchButton_Click(object sender, EventArgs e)
        {
            if (profilesGridView.SelectedRows.Count == 0)
            {
                MessageBox.Show("Select a profile!");
                return;
            }

            string name = profilesGridView.SelectedRows[0].Cells["Name"].Value.ToString();
            var selectedProfile = profiles.FirstOrDefault(p => p.Name == name);
            if (selectedProfile != null)
            {
                statusLabel.Text = $"🚀 Launching '{name}'...";

                if (selectedProfile.Platform == "Instagram")
                {
                    var botForm = new InstagramBotForm(selectedProfile);
                    botForm.Show();
                    statusLabel.Text = $"✅ Bot launched for '{name}'!";
                }
                else if (selectedProfile.Platform == "TikTok")
                {
                    var botForm = new TiktokBotForm(selectedProfile);
                    botForm.Show();
                    statusLabel.Text = $"✅ Bot launched for '{name}'!";
                }
            }
        }

        private void ProfilesGridView_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (e.ColumnIndex == profilesGridView.Columns["Edit"].Index ||
                e.ColumnIndex == profilesGridView.Columns["Config"].Index ||
                e.ColumnIndex == profilesGridView.Columns["Delete"].Index)
            {
                hoveredCell = new Point(e.ColumnIndex, e.RowIndex);
                profilesGridView.InvalidateCell(e.ColumnIndex, e.RowIndex);
                profilesGridView.Cursor = Cursors.Hand;
            }
        }

        private void ProfilesGridView_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (e.ColumnIndex == profilesGridView.Columns["Edit"].Index ||
                e.ColumnIndex == profilesGridView.Columns["Config"].Index ||
                e.ColumnIndex == profilesGridView.Columns["Delete"].Index)
            {
                hoveredCell = new Point(-1, -1);
                profilesGridView.InvalidateCell(e.ColumnIndex, e.RowIndex);
                profilesGridView.Cursor = Cursors.Default;
            }
        }

        private Color DarkenColor(Color color, int amount)
        {
            return Color.FromArgb(
                color.A,
                Math.Max(0, color.R - amount),
                Math.Max(0, color.G - amount),
                Math.Max(0, color.B - amount)
            );
        }

        private async void ScheduleButton_Click(object sender, EventArgs e)
        {
            try
            {
                await scheduleService.ToggleAsync();

                if (scheduleService.IsRunning)
                {
                    scheduleButton.Text = "⏹️ End Schedule";
                    scheduleButton.BackColor = Color.FromArgb(244, 67, 54);
                    statusLabel.Text = "✅ Scheduler started - Bots will open automatically";
                    statusLabel.ForeColor = Color.FromArgb(76, 175, 80);
                }
                else
                {
                    scheduleButton.Text = "⏰ Start Schedule";
                    scheduleButton.BackColor = Color.FromArgb(76, 175, 80);
                    statusLabel.Text = "⏸️ Scheduler stopped";
                    statusLabel.ForeColor = Color.FromArgb(180, 180, 180);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scheduler error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                scheduleLogTextBox.AppendText($"[Schedule] ERROR: {ex.Message}\r\n");
            }
        }

        private void StoryButton_Click(object sender, EventArgs e)
        {
            if (profilesGridView.SelectedRows.Count == 0)
            {
                MessageBox.Show("Select a profile first!");
                return;
            }

            string name = profilesGridView.SelectedRows[0].Cells["Name"].Value.ToString();
            var selectedProfile = profiles.FirstOrDefault(p => p.Name == name);

            if (selectedProfile != null)
            {
                statusLabel.Text = $"📱 Opening Story Poster for '{name}'...";

                if (selectedProfile.Platform == "Instagram")
                {
                    var storyForm = new StoryPosterForm(selectedProfile);
                    storyForm.Show();
                    statusLabel.Text = $"✅ Story Poster opened for '{name}'!";
                }
                else if (selectedProfile.Platform == "TikTok")
                {
                    MessageBox.Show("TikTok story posting coming soon!");
                }
                else
                {
                    MessageBox.Show("Platform not supported for stories.");
                }
            }
        }

        private void LoadDataFiles()
        {
            try
            {
                dataFilesComboBox.Items.Clear();

                string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                    dataFilesComboBox.Items.Add("(No files)");
                    return;
                }

                string[] extensions = { "*.txt", "*.csv" };
                var files = new List<string>();

                foreach (var ext in extensions)
                {
                    files.AddRange(Directory.GetFiles(dataPath, ext));
                }

                if (files.Count == 0)
                {
                    dataFilesComboBox.Items.Add("(No files)");
                    return;
                }

                foreach (var file in files.OrderBy(f => f))
                {
                    dataFilesComboBox.Items.Add(Path.GetFileName(file));
                }

                if (dataFilesComboBox.Items.Count > 0)
                {
                    dataFilesComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading Data files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenDataFileButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (dataFilesComboBox.SelectedItem == null ||
                    dataFilesComboBox.SelectedItem.ToString() == "(No files)")
                {
                    MessageBox.Show("No file selected!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string fileName = dataFilesComboBox.SelectedItem.ToString();
                string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                string fullPath = Path.Combine(dataPath, fileName);

                if (!File.Exists(fullPath))
                {
                    MessageBox.Show($"File not found: {fileName}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    LoadDataFiles();
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });

                statusLabel.Text = $"📂 Opened: {fileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Sergoe?.Dispose();
                Sergoe?.Dispose();
                Sergoe?.Dispose();
                modernToolTip?.Dispose();
            }
            base.Dispose(disposing);
        }

        public class TransparentDataGridView : DataGridView
        {
            public TransparentDataGridView()
            {
                this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                this.DoubleBuffered = true;
            }

            protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
            {
                base.OnCellPainting(e);
            }
        }

        public class TransparentTextBox : TextBox
        {
            public TransparentTextBox()
            {
                this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            }
        }
    }

    // ✅ Edit Profile Form - UNE SEULE DÉFINITION
    public class EditProfileForm : Form
    {
        private TextBox nameTextBox;
        private TextBox proxyTextBox;
        private TextBox groupNameTextBox;
        private Button saveButton;
        private Button cancelButton;

        public string ProfileName => nameTextBox.Text.Trim();
        public string ProxyValue => proxyTextBox.Text.Trim();
        public string GroupNameValue => groupNameTextBox.Text.Trim();

        public EditProfileForm(Profile profile)
        {
            this.Text = $"Edit Profile: {profile.Name}";
            this.Size = new Size(400, 240);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(24, 24, 24);

            Label nameLabel = new Label
            {
                Text = "Name:",
                Location = new Point(20, 20),
                Size = new Size(80, 25),
                ForeColor = Color.White
            };

            nameTextBox = new TextBox
            {
                Text = profile.Name,
                Location = new Point(110, 20),
                Size = new Size(250, 25),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label proxyLabel = new Label
            {
                Text = "Proxy:",
                Location = new Point(20, 60),
                Size = new Size(80, 25),
                ForeColor = Color.White
            };

            proxyTextBox = new TextBox
            {
                Text = profile.Proxy ?? "",
                Location = new Point(110, 60),
                Size = new Size(250, 25),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label groupLabel = new Label
            {
                Text = "Group:",
                Location = new Point(20, 100),
                Size = new Size(80, 25),
                ForeColor = Color.White
            };

            groupNameTextBox = new TextBox
            {
                Text = profile.GroupName ?? "",
                Location = new Point(110, 100),
                Size = new Size(250, 25),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Leave empty for solo"
            };

            saveButton = new Button
            {
                Text = "✅ Save",
                Location = new Point(110, 150),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK,
                Cursor = Cursors.Hand
            };
            saveButton.FlatAppearance.BorderSize = 0;

            cancelButton = new Button
            {
                Text = "❌ Cancel",
                Location = new Point(220, 150),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(244, 67, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel,
                Cursor = Cursors.Hand
            };
            cancelButton.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[] {
                nameLabel, nameTextBox,
                proxyLabel, proxyTextBox,
                groupLabel, groupNameTextBox,
                saveButton, cancelButton
            });

            this.AcceptButton = saveButton;
            this.CancelButton = cancelButton;
        }
    }
}