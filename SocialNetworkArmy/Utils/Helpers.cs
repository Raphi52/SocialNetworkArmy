// Utils/Helpers.cs (ajout du using)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsvHelper;
using SocialNetworkArmy.Models; // Ajouté pour ScheduleEntry

namespace SocialNetworkArmy.Utils
{
    public static class Helpers
    {
        private static readonly Random Rand = new Random();

        public static List<string> LoadTargets(string filePath)
        {
            if (File.Exists(filePath))
            {
                return File.ReadAllLines(filePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            }
            return new List<string>();
        }

        public static List<ScheduleEntry> LoadSchedule(string filePath)
        {
            if (File.Exists(filePath))
            {
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture))
                {
                    return csv.GetRecords<ScheduleEntry>().ToList();
                }
            }
            return new List<ScheduleEntry>();
        }

        public static int RandomDelay(int min, int max)
        {
            return Rand.Next(min, max + 1);
        }

        // More helpers for random comments, etc.
        public static string GenerateRandomComment()
        {
            var comments = new[] { "Great!", "Love it! ❤️", "Awesome content!", "Keep it up!" };
            return comments[Rand.Next(comments.Length)];
        }
    }
}