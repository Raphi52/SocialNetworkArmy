using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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

        private ListBox profilesListBox;
        private ComboBox platformComboBox;
        private TextBox profileNameTextBox;
        private TextBox proxyTextBox;
        private Button createButton;
        private Button deleteButton;
        private Button launchButton;
        private Label statusLabel;

        public MainForm()
        {
            InitializeComponent();
            profileService = new ProfileService();
            fingerprintService = new FingerprintService();
            profiles = profileService.LoadProfiles();
            PopulateProfilesList();
        }

        private void InitializeComponent()
        {
            this.AutoScaleDimensions = new SizeF(8F, 20F);
            this.AutoScaleMode = AutoScaleMode.Font;

            profilesListBox = new ListBox { Location = new Point(12, 12), Size = new Size(300, 200) };
            profilesListBox.SelectedIndexChanged += ProfilesListBox_SelectedIndexChanged;

            platformComboBox = new ComboBox { Location = new Point(12, 220), Size = new Size(100, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            platformComboBox.Items.AddRange(new object[] { "Instagram", "TikTok" });

            profileNameTextBox = new TextBox { Location = new Point(120, 220), Size = new Size(100, 23) };
            profileNameTextBox.PlaceholderText = "Nom";

            proxyTextBox = new TextBox { Location = new Point(230, 220), Size = new Size(150, 23) };
            proxyTextBox.PlaceholderText = "Proxy (optionnel)";

            createButton = new Button { Text = "Cr�er", Location = new Point(12, 250), Size = new Size(100, 30), UseVisualStyleBackColor = true };
            createButton.Click += CreateButton_Click;

            deleteButton = new Button { Text = "Supprimer", Location = new Point(120, 250), Size = new Size(100, 30), UseVisualStyleBackColor = true };
            deleteButton.Click += DeleteButton_Click;

            launchButton = new Button { Text = "Lancer", Location = new Point(230, 250), Size = new Size(100, 30), UseVisualStyleBackColor = true };
            launchButton.Click += LaunchButton_Click;

            statusLabel = new Label { Location = new Point(12, 290), Size = new Size(400, 20), Text = "Pr�t", AutoSize = true };

            this.ClientSize = new Size(430, 330);
            this.Text = "SocialNetworkArmy";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            this.Controls.Add(profilesListBox);
            this.Controls.Add(platformComboBox);
            this.Controls.Add(profileNameTextBox);
            this.Controls.Add(proxyTextBox);
            this.Controls.Add(createButton);
            this.Controls.Add(deleteButton);
            this.Controls.Add(launchButton);
            this.Controls.Add(statusLabel);
        }

        private void PopulateProfilesList()
        {
            profilesListBox.Items.Clear();
            foreach (var profile in profiles)
            {
                profilesListBox.Items.Add($"{profile.Name} ({profile.Platform}) - Proxy: {profile.Proxy ?? "Local"}");
            }
            statusLabel.Text = $"Profils : {profiles.Count}";
        }

        private void CreateButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(profileNameTextBox.Text) || platformComboBox.SelectedItem == null)
            {
                MessageBox.Show("Remplis nom et plateforme !");
                return;
            }

            string name = profileNameTextBox.Text.Trim();
            string platform = platformComboBox.SelectedItem.ToString();
            string proxy = string.IsNullOrWhiteSpace(proxyTextBox.Text) ? "" : proxyTextBox.Text.Trim();

            if (profiles.Any(p => p.Name == name))
            {
                MessageBox.Show("Nom existant !");
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
            Logger.LogInfo($"Profil '{name}' cr��.");
            statusLabel.Text = $"Profil '{name}' cr�� !";
            profileNameTextBox.Clear();
            proxyTextBox.Clear();
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (profilesListBox.SelectedIndex < 0) // Corrig� : Check index pour �viter OutOfRange
            {
                MessageBox.Show("S�lectionne un profil !");
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
                Logger.LogInfo($"Profil '{name}' supprim�.");
                statusLabel.Text = $"Profil '{name}' supprim� !";
            }
        }

        private async void LaunchButton_Click(object sender, EventArgs e)
        {
            if (profilesListBox.SelectedIndex < 0) // Corrig� : Check index pour �viter OutOfRange
            {
                MessageBox.Show("S�lectionne un profil !");
                return;
            }

            string selected = profilesListBox.SelectedItem.ToString();
            string name = selected.Split(' ')[0];
            var selectedProfile = profiles.FirstOrDefault(p => p.Name == name);
            if (selectedProfile != null)
            {
                statusLabel.Text = $"Lancement '{name}'...";

                Form botForm = selectedProfile.Platform == "Instagram" ? new InstagramBotForm(selectedProfile) : new TikTokBotForm(selectedProfile);
                botForm.Show();

                statusLabel.Text = $"Bot lanc� pour '{name}' !";
            }
        }

        private void ProfilesListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (profilesListBox.SelectedIndex < 0) return; // Corrig� : Skip si pas s�lectionn� (�vite OutOfRange)
            // Optionnel : Log ou load details selected
        }
    }
}