using System.Collections.Generic;

namespace SocialNetworkArmy.Models
{
    public class Fingerprint
    {
        public string UserAgent { get; set; }
        public string Timezone { get; set; }
        public List<string> Languages { get; set; }
        public string ScreenResolution { get; set; }
        public string Viewport { get; set; }
        public string WebGLVendor { get; set; }
        public string WebGLRenderer { get; set; }
        public string AudioContext { get; set; }
        public List<string> Fonts { get; set; }
        public int HardwareConcurrency { get; set; }
        public string Platform { get; set; }
        public string Vendor { get; set; }
        public int ScreenDepth { get; set; }
        public int MaxTouchPoints { get; set; }
    }
}