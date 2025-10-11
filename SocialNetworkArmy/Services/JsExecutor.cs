using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    public class JsExecutor
    {
        private readonly WebView2 _webView;
        private readonly Action<string> _log;

        public JsExecutor(WebView2 webView, Action<string> log)
        {
            _webView = webView;
            _log = log;
        }

        public async Task<string> ExecJsAsync(string js, CancellationToken token, string tag = null)
        {
            string wrapped = @"
(function(){
  try {
    var __val = (function(){ " + js + @" })();
    if (typeof __val === 'undefined') return JSON.stringify('undefined');
    if (typeof __val === 'string') return JSON.stringify(__val);
    try { return JSON.stringify(__val); } catch(e) { return JSON.stringify(String(__val)); }
  } catch(e) {
    try { return JSON.stringify('ERR:'+(e && e.message ? e.message : e)); } catch(_){
      return JSON.stringify('ERR');
    }
  }
})();";

            var res = await ExecuteScriptWithCancellationAsync(_webView, wrapped, token);
            if (!string.IsNullOrEmpty(tag)) _log(tag + res);
            return UnQ(res ?? "\"undefined\"");
        }

        private static async Task<string> ExecuteScriptWithCancellationAsync(WebView2 webView, string script, CancellationToken token)
        {
            if (webView?.CoreWebView2 == null) throw new InvalidOperationException("WebView2 non initialisé.");
            var execTask = webView.ExecuteScriptAsync(script);
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (token.Register(() =>
            {
                try { webView.CoreWebView2.Stop(); } catch { }
                cancelTcs.TrySetResult(true);
            }))
            {
                var done = await Task.WhenAny(execTask, cancelTcs.Task).ConfigureAwait(true);
                if (done == cancelTcs.Task) throw new OperationCanceledException(token);
                return await execTask.ConfigureAwait(true);
            }
        }

        private static string UnQ(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            while (s.Length >= 2 &&
                   ((s[0] == '"' && s[s.Length - 1] == '"') ||
                    (s.StartsWith("\\\"") && s.EndsWith("\\\""))))
            {
                if (s.StartsWith("\\\"") && s.EndsWith("\\\""))
                    s = s.Substring(2, s.Length - 4);
                else
                    s = s.Substring(1, s.Length - 2);
                s = s.Trim();
            }
            return s;
        }
    }
}   