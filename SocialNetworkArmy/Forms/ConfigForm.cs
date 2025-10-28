using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;

namespace SocialNetworkArmy.Forms
{
    public class ConfigForm : Form
    {
        private readonly AccountConfig config;
        private readonly string accountName;

        // UI Controls
        private NumericUpDown minCommentsInput;
        private CheckedListBox languagesCheckedList;
        private NumericUpDown maxPostAgeInput;
        private ComboBox nicheComboBox;
        private Button saveButton;
        private Button cancelButton;

        public ConfigForm(string accountName)
        {
            this.accountName = accountName;
            this.config = ConfigService.LoadConfig(accountName);

            InitializeComponent();
            LoadConfigToUI();
        }

        private void InitializeComponent()
        {
            this.Text = $"Configuration - {accountName}";
            this.Size = new Size(500, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.ForeColor = Color.White;

            var font = new Font("Segoe UI", 10f);

            // Title
            var titleLabel = new Label
            {
                Text = $"Account Configuration: {accountName}",
                Location = new Point(20, 20),
                Size = new Size(450, 30),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 150, 255)
            };
            this.Controls.Add(titleLabel);

            // Min Comments Section
            var minCommentsLabel = new Label
            {
                Text = "Minimum Comments to Comment:",
                Location = new Point(20, 70),
                Size = new Size(250, 25),
                Font = font
            };
            this.Controls.Add(minCommentsLabel);

            minCommentsInput = new NumericUpDown
            {
                Location = new Point(280, 68),
                Size = new Size(180, 25),
                Minimum = 0,
                Maximum = 10000,
                Increment = 50,
                Value = 300,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = font
            };
            this.Controls.Add(minCommentsInput);

            // Target Languages Section
            var languagesLabel = new Label
            {
                Text = "Target Languages:",
                Location = new Point(20, 110),
                Size = new Size(250, 25),
                Font = font
            };
            this.Controls.Add(languagesLabel);

            languagesCheckedList = new CheckedListBox
            {
                Location = new Point(20, 140),
                Size = new Size(440, 120),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = font,
                CheckOnClick = true
            };
            languagesCheckedList.Items.AddRange(new object[]
            {
                "Any (All Languages)",
                "English",
                "French",
                "Spanish",
                "Portuguese",
                "German"
            });
            this.Controls.Add(languagesCheckedList);

            // Max Post Age Section
            var maxAgeLabel = new Label
            {
                Text = "Max Post Age (hours):",
                Location = new Point(20, 275),
                Size = new Size(250, 25),
                Font = font
            };
            this.Controls.Add(maxAgeLabel);

            maxPostAgeInput = new NumericUpDown
            {
                Location = new Point(280, 273),
                Size = new Size(180, 25),
                Minimum = 0,
                Maximum = 24,
                Increment = 1,
                Value = 24,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = font
            };
            this.Controls.Add(maxPostAgeInput);

            // Niche Section
            var nicheLabel = new Label
            {
                Text = "Content Niche Filter:",
                Location = new Point(20, 315),
                Size = new Size(250, 25),
                Font = font
            };
            this.Controls.Add(nicheLabel);

            nicheComboBox = new ComboBox
            {
                Location = new Point(280, 313),
                Size = new Size(180, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = font
            };
            nicheComboBox.Items.AddRange(new object[] { "Any", "Girls" });
            this.Controls.Add(nicheComboBox);

            // Save Button
            saveButton = new Button
            {
                Text = "💾 Save",
                Location = new Point(260, 365),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.Click += SaveButton_Click;
            this.Controls.Add(saveButton);

            // Cancel Button
            cancelButton = new Button
            {
                Text = "❌ Cancel",
                Location = new Point(370, 365),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            this.Controls.Add(cancelButton);

            // Handle "Any" checkbox behavior
            languagesCheckedList.ItemCheck += LanguagesCheckedList_ItemCheck;
        }

        private void LanguagesCheckedList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            // If "Any" is being checked, uncheck all others
            if (e.Index == 0 && e.NewValue == CheckState.Checked)
            {
                this.BeginInvoke(new Action(() =>
                {
                    for (int i = 1; i < languagesCheckedList.Items.Count; i++)
                    {
                        languagesCheckedList.SetItemChecked(i, false);
                    }
                }));
            }
            // If another language is being checked, uncheck "Any"
            else if (e.Index > 0 && e.NewValue == CheckState.Checked)
            {
                this.BeginInvoke(new Action(() =>
                {
                    languagesCheckedList.SetItemChecked(0, false);
                }));
            }
        }

        private void LoadConfigToUI()
        {
            // Load min comments
            minCommentsInput.Value = config.MinCommentsToComment;

            // Load languages
            bool hasAny = config.TargetLanguages.Contains("Any");
            languagesCheckedList.SetItemChecked(0, hasAny);

            if (!hasAny)
            {
                if (config.TargetLanguages.Contains("English"))
                    languagesCheckedList.SetItemChecked(1, true);
                if (config.TargetLanguages.Contains("French"))
                    languagesCheckedList.SetItemChecked(2, true);
                if (config.TargetLanguages.Contains("Spanish"))
                    languagesCheckedList.SetItemChecked(3, true);
                if (config.TargetLanguages.Contains("Portuguese"))
                    languagesCheckedList.SetItemChecked(4, true);
                if (config.TargetLanguages.Contains("German"))
                    languagesCheckedList.SetItemChecked(5, true);
            }

            // Load max post age
            maxPostAgeInput.Value = config.MaxPostAgeHours;

            // Load niche
            nicheComboBox.SelectedItem = config.Niche;
            if (nicheComboBox.SelectedItem == null)
                nicheComboBox.SelectedItem = "Girls"; // Default
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Save min comments
                config.MinCommentsToComment = (int)minCommentsInput.Value;

                // Save languages
                config.TargetLanguages.Clear();
                if (languagesCheckedList.GetItemChecked(0)) // Any
                {
                    config.TargetLanguages.Add("Any");
                }
                else
                {
                    if (languagesCheckedList.GetItemChecked(1)) config.TargetLanguages.Add("English");
                    if (languagesCheckedList.GetItemChecked(2)) config.TargetLanguages.Add("French");
                    if (languagesCheckedList.GetItemChecked(3)) config.TargetLanguages.Add("Spanish");
                    if (languagesCheckedList.GetItemChecked(4)) config.TargetLanguages.Add("Portuguese");
                    if (languagesCheckedList.GetItemChecked(5)) config.TargetLanguages.Add("German");
                }

                // Ensure at least one language is selected
                if (config.TargetLanguages.Count == 0)
                {
                    MessageBox.Show("Please select at least one target language.", "Validation Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Save max post age
                config.MaxPostAgeHours = (int)maxPostAgeInput.Value;

                // Save niche
                config.Niche = nicheComboBox.SelectedItem.ToString();

                // Save to file
                ConfigService.SaveConfig(config);

                MessageBox.Show("Configuration saved successfully!", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
