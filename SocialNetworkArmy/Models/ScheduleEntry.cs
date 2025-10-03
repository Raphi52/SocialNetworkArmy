using System;

namespace SocialNetworkArmy.Models
{
    public record ScheduleEntry
    {
        public DateTime Date { get; init; }
        public string Account { get; init; }
        public string Platform { get; init; }
        public string MediaPath { get; init; }
        public string Description { get; init; }
    }
}