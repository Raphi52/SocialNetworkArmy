// Program.cs
using System;
using System.Windows.Forms;
using SocialNetworkArmy.Forms; // Ajout pour importer MainForm

namespace SocialNetworkArmy
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {

            ApplicationConfiguration.Initialize(); // .NET 8 standard (remplace EnableVisualStyles/SetCompatibleTextRenderingDefault)
            Application.SetHighDpiMode(HighDpiMode.SystemAware); // Pour scaling écran
            Application.Run(new MainForm()); // Maintenant trouve MainForm via using
        }
    }
}