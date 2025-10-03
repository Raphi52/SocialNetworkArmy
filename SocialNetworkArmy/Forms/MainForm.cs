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

        private Font yaheiBold12 = new Font("Microsoft YaHei", 11f, FontStyle.Bold); // Police globale YaHei 12 gras

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
            // Dark mode : Fond sombre pour la form
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Font = yaheiBold12; // Police globale sur la form (propagé aux enfants)

            this.AutoScaleDimensions = new SizeF(8F, 20F);
            this.AutoScaleMode = AutoScaleMode.Font;

            // ListBox élargie pour quasi toute la largeur (460 sur 500)
            profilesListBox = new ListBox { Location = new Point(12, 12), Size = new Size(460, 250), BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, Font = yaheiBold12 };
            profilesListBox.SelectedIndexChanged += ProfilesListBox_SelectedIndexChanged;

            platformComboBox = new ComboBox { Location = new Point(12, 280), Size = new Size(120, 30), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, Font = yaheiBold12 };
            platformComboBox.Items.AddRange(new object[] { "Instagram", "TikTok" });

            profileNameTextBox = new TextBox { Location = new Point(140, 280), Size = new Size(120, 30), BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = yaheiBold12 };
            profileNameTextBox.PlaceholderText = "Nom";

            proxyTextBox = new TextBox { Location = new Point(270, 280), Size = new Size(200, 30), BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = yaheiBold12 };
            proxyTextBox.PlaceholderText = "Proxy (optionnel)";

            // Boutons agrandis (110x35)
            // Bouton Créer : Fond sombre, bordure bleue
            createButton = new Button { Text = "Créer", Location = new Point(12, 320), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            createButton.FlatAppearance.BorderSize = 2;
            createButton.FlatAppearance.BorderColor = Color.FromArgb(33, 150, 243);
            createButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            createButton.Click += CreateButton_Click;

            // Bouton Supprimer : Fond sombre, bordure rouge
            deleteButton = new Button { Text = "Supprimer", Location = new Point(132, 320), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            deleteButton.FlatAppearance.BorderSize = 2;
            deleteButton.FlatAppearance.BorderColor = Color.FromArgb(244, 67, 54);
            deleteButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            deleteButton.Click += DeleteButton_Click;

            // Bouton Lancer : Fond sombre, bordure verte
            launchButton = new Button { Text = "Lancer", Location = new Point(252, 320), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            launchButton.FlatAppearance.BorderSize = 2;
            launchButton.FlatAppearance.BorderColor = Color.FromArgb(76, 175, 80);
            launchButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            launchButton.Click += LaunchButton_Click;

            statusLabel = new Label { Location = new Point(12, 370), Size = new Size(400, 20), Text = "Prêt", AutoSize = true, ForeColor = Color.LightGray, Font = yaheiBold12 };

            this.ClientSize = new Size(500, 420); // Form agrandie
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
            Logger.LogInfo($"Profil '{name}' créé.");
            statusLabel.Text = $"Profil '{name}' créé !";
            profileNameTextBox.Clear();
            proxyTextBox.Clear();
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (profilesListBox.SelectedIndex < 0)
            {
                MessageBox.Show("Sélectionne un profil !");
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
                Logger.LogInfo($"Profil '{name}' supprimé.");
                statusLabel.Text = $"Profil '{name}' supprimé !";
            }
        }

        private async void LaunchButton_Click(object sender, EventArgs e)
        {
            if (profilesListBox.SelectedIndex < 0)
            {
                MessageBox.Show("Sélectionne un profil !");
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

                statusLabel.Text = $"Bot lancé pour '{name}' !";
            }
        }

        private void ProfilesListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Optionnel : Log ou load details selected
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                yaheiBold12.Dispose(); // Clean font
            }
            base.Dispose(disposing);
        }
    }
}