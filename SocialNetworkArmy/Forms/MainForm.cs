using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Forms
{
    public partial class MainForm : Form
    {
        private List<Profile> profiles;
        private readonly ProfileService profileService;
        private readonly FingerprintService fingerprintService;
        private ScheduleService scheduleService;

        private ListBox profilesListBox;
        private ComboBox platformComboBox;
        private TextBox profileNameTextBox;
        private TextBox proxyTextBox;
        private Button createButton;
        private Button deleteButton;
        private Button launchButton;
        private Button scheduleButton;
        private Label statusLabel;
        private TextBox scheduleLogTextBox;
        private ComboBox dataFilesComboBox;
        private Button openDataFileButton;

        private Font yaheiBold12 = new Font("Microsoft YaHei", 9f, FontStyle.Bold);
        private Font yaheiBold10 = new Font("Microsoft YaHei", 9f, FontStyle.Bold);

        public MainForm()
        {
            InitializeComponent();
            this.Icon = new System.Drawing.Icon("Data\\Icons\\MainForm.ico");
            profileService = new ProfileService();
            fingerprintService = new FingerprintService();
            profiles = profileService.LoadProfiles();

            // Initialize ScheduleService with the log textbox
            scheduleService = new ScheduleService(scheduleLogTextBox, profileService);

            PopulateProfilesList();
            LoadDataFiles();
        }

        private void InitializeComponent()
        {
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Font = yaheiBold12;
            this.AutoScaleDimensions = new SizeF(8F, 20F);
            this.AutoScaleMode = AutoScaleMode.Font;

            // ProfilesListBox - moved up slightly
            profilesListBox = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(960, 200),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = yaheiBold12
            };
            profilesListBox.SelectedIndexChanged += ProfilesListBox_SelectedIndexChanged;

            // Platform ComboBox
            platformComboBox = new ComboBox
            {
                Location = new Point(12, 225),
                Size = new Size(120, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = yaheiBold12
            };
            platformComboBox.Items.AddRange(new object[] { "Instagram", "TikTok" });

            // Profile Name TextBox
            profileNameTextBox = new TextBox
            {
                Location = new Point(140, 225),
                Size = new Size(120, 30),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = yaheiBold12,
                PlaceholderText = "Name"
            };

            // Proxy TextBox
            proxyTextBox = new TextBox
            {
                Location = new Point(270, 225),
                Size = new Size(400, 30),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = yaheiBold12,
                PlaceholderText = "Proxy (optional)"
            };

            // Buttons Row
            int buttonY = 265;
            Size btnSize = new Size(145, 45);

            // Create Button
            createButton = new Button
            {
                Text = "Create",
                Location = new Point(12, buttonY),
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold10
            };
            createButton.FlatAppearance.BorderSize = 2;
            createButton.FlatAppearance.BorderColor = Color.FromArgb(33, 150, 243); // Blue
            createButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            createButton.Click += CreateButton_Click;

            // Delete Button
            deleteButton = new Button
            {
                Text = "Delete",
                Location = new Point(167, buttonY),
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold10
            };
            deleteButton.FlatAppearance.BorderSize = 2;
            deleteButton.FlatAppearance.BorderColor = Color.FromArgb(244, 67, 54); // Red
            deleteButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            deleteButton.Click += DeleteButton_Click;

            // Launch Button
            launchButton = new Button
            {
                Text = "Open",
                Location = new Point(322, buttonY),
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold10
            };
            launchButton.FlatAppearance.BorderSize = 2;
            launchButton.FlatAppearance.BorderColor = Color.FromArgb(76, 175, 80); // Green
            launchButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            launchButton.Click += LaunchButton_Click;

            // Open Phone Button
            Button openPhone = new Button
            {
                Text = "Open Phone",
                Location = new Point(477, buttonY),
                Size = new Size(145, 45),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold10
            };
            openPhone.FlatAppearance.BorderSize = 2;
            openPhone.FlatAppearance.BorderColor = Color.FromArgb(156, 39, 176); // Purple
            openPhone.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            openPhone.Click += StoryButton_Click;

            // Schedule Button
            scheduleButton = new Button
            {
                Text = "Start Schedule",
                Location = new Point(632, buttonY),
                Size = new Size(180, 45),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold10
            };
            scheduleButton.FlatAppearance.BorderSize = 2;
            scheduleButton.FlatAppearance.BorderColor = Color.FromArgb(255, 193, 7); // Yellow
            scheduleButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            scheduleButton.Click += ScheduleButton_Click;

            // Data Files ComboBox - Same row as inputs, on the right side
            dataFilesComboBox = new ComboBox
            {
                Location = new Point(790, 225),
                Size = new Size(180, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = yaheiBold10
            };

            // Open Data File Button - Same row as buttons, aligned to the right
            openDataFileButton = new Button
            {
                Text = "Open File",
                Location = new Point(820, buttonY),
                Size = new Size(150, 45),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold10
            };
            openDataFileButton.FlatAppearance.BorderSize = 2;
            openDataFileButton.FlatAppearance.BorderColor = Color.White;
            openDataFileButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            openDataFileButton.Click += OpenDataFileButton_Click;

            // Status Label
            statusLabel = new Label
            {
                Location = new Point(12, 320),
                Size = new Size(800, 20),
                Text = "Ready",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = yaheiBold10
            };

            // Schedule Log TextBox
            scheduleLogTextBox = new TextBox
            {
                Location = new Point(12, 350),
                Size = new Size(960, 140),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei", 8f, FontStyle.Regular),
                Text = "[Schedule] Ready. Click 'Schedule ON' to start global scheduler.\r\n"
            };

            // Form settings
            this.ClientSize = new Size(1000, 500);
            this.Text = "SocialNetworkArmy";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            // Add controls
            this.Controls.Add(profilesListBox);
            this.Controls.Add(platformComboBox);
            this.Controls.Add(profileNameTextBox);
            this.Controls.Add(proxyTextBox);
            this.Controls.Add(createButton);
            this.Controls.Add(deleteButton);
            this.Controls.Add(launchButton);
            this.Controls.Add(scheduleButton);
            this.Controls.Add(openPhone);
            this.Controls.Add(statusLabel);
            this.Controls.Add(scheduleLogTextBox);
            this.Controls.Add(dataFilesComboBox);
            this.Controls.Add(openDataFileButton);

            // Handle form closing
            this.FormClosing += MainForm_FormClosing;
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop scheduler if running
            if (scheduleService != null && scheduleService.IsRunning)
            {
                scheduleLogTextBox.AppendText("[Schedule] Shutting down scheduler...\r\n");
                await scheduleService.ToggleAsync(); // Stop
            }
        }

        private void PopulateProfilesList()
        {
            profilesListBox.Items.Clear();
            foreach (var profile in profiles)
            {
                profilesListBox.Items.Add($"{profile.Name} ({profile.Platform}) - Proxy: {profile.Proxy ?? "Local"}");
            }
            statusLabel.Text = $"Profiles: {profiles.Count}";
        }

        private void CreateButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(profileNameTextBox.Text) || platformComboBox.SelectedItem == null)
            {
                MessageBox.Show("Fill in name and platform!");
                return;
            }

            string name = profileNameTextBox.Text.Trim();
            if (!IsValidProfileName(name))
            {
                MessageBox.Show("Invalid name! Use letters, numbers, dashes or underscores only (3-20 characters).");
                return;
            }

            string platform = platformComboBox.SelectedItem.ToString();
            string proxy = string.IsNullOrWhiteSpace(proxyTextBox.Text) ? "" : proxyTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(proxy) && !IsValidProxy(proxy))
            {
                MessageBox.Show("Invalid proxy format! Ex: IP:port or http://user:pass@IP:port");
                return;
            }

            if (profiles.Any(p => p.Name == name))
            {
                MessageBox.Show("Name already exists!");
                return;
            }

            var fingerprint = fingerprintService.GenerateDesktopFingerprint();
            var serializedFingerprint = JsonConvert.SerializeObject(fingerprint);
            var newProfile = new Profile
            {
                Name = name,
                Platform = platform,
                Proxy = proxy,
                Fingerprint = serializedFingerprint,
                StorageState = ""
            };

            profiles.Add(newProfile);
            profileService.SaveProfiles(profiles);
            PopulateProfilesList();
            statusLabel.Text = $"Profile '{name}' created!";
            profileNameTextBox.Clear();
            proxyTextBox.Clear();
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

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (profilesListBox.SelectedIndex < 0)
            {
                MessageBox.Show("Select a profile!");
                return;
            }

            string selected = profilesListBox.SelectedItem.ToString();
            string name = selected.Split(' ')[0];
            var profileToDelete = profiles.FirstOrDefault(p => p.Name == name);
            if (profileToDelete != null)
            {
                profiles.Remove(profileToDelete);
                profileService.SaveProfiles(profiles);
                PopulateProfilesList();
                statusLabel.Text = $"Profile '{name}' deleted!";
            }
        }

        private void LaunchButton_Click(object sender, EventArgs e)
        {
            if (profilesListBox.SelectedIndex < 0)
            {
                MessageBox.Show("Select a profile!");
                return;
            }

            string selected = profilesListBox.SelectedItem.ToString();
            string name = selected.Split(' ')[0];
            var selectedProfile = profiles.FirstOrDefault(p => p.Name == name);
            if (selectedProfile != null)
            {
                statusLabel.Text = $"Launching '{name}'...";

                if (selectedProfile.Platform == "Instagram")
                {
                    var botForm = new InstagramBotForm(selectedProfile);
                    botForm.Show();
                    statusLabel.Text = $"Bot launched for '{name}'!";
                }
                else if (selectedProfile.Platform == "TikTok")
                {
                    var botForm = new TiktokBotForm(selectedProfile);
                    botForm.Show();
                    statusLabel.Text = $"Bot launched for '{name}'!";
                }
            }
        }

        private async void ScheduleButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Toggle scheduler ON/OFF
                await scheduleService.ToggleAsync();

                // Update button text and color based on state
                if (scheduleService.IsRunning)
                {
                    scheduleButton.Text = "End Schedule";
                    scheduleButton.FlatAppearance.BorderColor = Color.FromArgb(76, 175, 80); // Green
                    statusLabel.Text = "Scheduler started - Bots will open automatically";
                    statusLabel.ForeColor = Color.LightGreen;
                }
                else
                {
                    scheduleButton.Text = "Start Schedule";
                    scheduleButton.FlatAppearance.BorderColor = Color.FromArgb(255, 193, 7); // Yellow
                    statusLabel.Text = "Scheduler stopped";
                    statusLabel.ForeColor = Color.LightGray;
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
            if (profilesListBox.SelectedIndex < 0)
            {
                MessageBox.Show("Select a profile first!");
                return;
            }

            string selected = profilesListBox.SelectedItem.ToString();
            string name = selected.Split(' ')[0];
            var selectedProfile = profiles.FirstOrDefault(p => p.Name == name);

            if (selectedProfile != null)
            {
                statusLabel.Text = $"Opening Story Poster for '{name}'...";

                if (selectedProfile.Platform == "Instagram")
                {
                    var storyForm = new StoryPosterForm(selectedProfile);
                    storyForm.Show();
                    statusLabel.Text = $"Story Poster opened for '{name}'!";
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

                // Charger tous les fichiers .txt, .csv, .json
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

                // Ajouter seulement les noms de fichiers (pas le chemin complet)
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
                    LoadDataFiles(); // Rafraîchir la liste
                    return;
                }

                // Ouvrir avec le programme par défaut
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });

                statusLabel.Text = $"Opened: {fileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ProfilesListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Optional: Load profile details
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                yaheiBold12.Dispose();
                yaheiBold10.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}