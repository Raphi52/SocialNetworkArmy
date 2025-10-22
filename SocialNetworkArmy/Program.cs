// Program.cs
using Microsoft.Web.WebView2.Core;
using SocialNetworkArmy.Forms; // Ajout pour importer MainForm
using System;
using System.Windows.Forms;

namespace SocialNetworkArmy
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            if (!CheckWebView2Runtime())
            {
                return;
            }

            Application.EnableVisualStyles();
            ApplicationConfiguration.Initialize(); // .NET 8 standard (remplace EnableVisualStyles/SetCompatibleTextRenderingDefault)
            Application.SetHighDpiMode(HighDpiMode.SystemAware); // Pour scaling écran
            Application.Run(new MainForm()); // Maintenant trouve MainForm via using
        }
        private static bool CheckWebView2Runtime()
        {
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch
            {
                MessageBox.Show(
                    "WebView2 Runtime is not installed.\n\n" +
                    "Download it from:\n" +
                    "https://go.microsoft.com/fwlink/p/?LinkId=2124703\n\n" +
                    "The application will now exit.",
                    "Missing Dependency",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
        }

    }
}