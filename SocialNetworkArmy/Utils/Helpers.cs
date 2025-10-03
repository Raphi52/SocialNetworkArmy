// Utils/Helpers.cs - Ajouts Poisson et randomisation
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsvHelper;
using SocialNetworkArmy.Models;

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

        // Poisson pour variance interactions
        public static int PoissonRandom(double lambda)
        {
            double L = Math.Exp(-lambda);
            double p = 1.0;
            int k = 0;
            do
            {
                k++;
                p *= Rand.NextDouble();
            } while (p > L);
            return k - 1;
        }

        public static double RandomInteractionRate(int basePercent, double variance = 0.2)
        {
            var poissonVar = PoissonRandom(1.0);
            return basePercent / 100.0 * (1 + (poissonVar - 1) * variance);
        }

        public static string GenerateRandomComment()
        {
            var comments = new[] { "Great!", "Love it! ❤️", "Awesome content!", "Keep it up!" };
            return comments[Rand.Next(comments.Length)];
        }

        // Randomise ordre actions (ex: like/comment)
        public static List<string> RandomizeActionOrder(List<string> actions)
        {
            var shuffled = actions.ToList();
            for (int i = 0; i < shuffled.Count; i++)
            {
                int j = Rand.Next(i, shuffled.Count);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            return shuffled;
        }
    }
}