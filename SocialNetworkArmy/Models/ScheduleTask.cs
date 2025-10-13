using System;

namespace SocialNetworkArmy.Models
{
    public class ScheduledTask
    {
        public DateTime Date { get; set; }
        public string Platform { get; set; }
        public string Account { get; set; }
        public string Activity { get; set; }
        public string MediaPath { get; set; }
        public string Description { get; set; }
        public bool Executed { get; set; }
    }
}