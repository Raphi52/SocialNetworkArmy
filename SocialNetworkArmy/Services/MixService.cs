using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    internal class MixService
    {
        private WebView2 webView;
        private TextBox logTextBox;
        private Profile profile;
        private InstagramBotForm instagramBotForm;

        public MixService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm instagramBotForm)
        {
            this.webView = webView;
            this.logTextBox = logTextBox;
            this.profile = profile;
            this.instagramBotForm = instagramBotForm;
        }

        internal async Task RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}
