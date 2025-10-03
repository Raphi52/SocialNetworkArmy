// Models/Statistics.cs
using System;
using Newtonsoft.Json;

namespace SocialNetworkArmy.Models
{
    public class Statistics
    {
        [JsonProperty("profileName")]
        public string ProfileName { get; set; } // Nom du profil pour associer les stats

        [JsonProperty("platform")]
        public string Platform { get; set; } // Instagram ou TikTok

        [JsonProperty("date")]
        public DateTime Date { get; set; } = DateTime.Today; // Par jour pour rotation

        [JsonProperty("likes")]
        public int Likes { get; set; } = 0; // Nb de likes effectués

        [JsonProperty("comments")]
        public int Comments { get; set; } = 0; // Nb de commentaires postés

        [JsonProperty("publications")]
        public int Publications { get; set; } = 0; // Nb de posts réussis

        [JsonProperty("views")]
        public int Views { get; set; } = 0; // Nb de Reels visionnés

        [JsonProperty("errors")]
        public int Errors { get; set; } = 0; // Nb d'erreurs/skips (profils privés, etc.)

        [JsonProperty("sessionDuration")]
        public TimeSpan SessionDuration { get; set; } = TimeSpan.Zero; // Durée totale session

        // Méthodes utilitaires (optionnelles)
        public void IncrementLikes() { Likes++; }
        public void IncrementComments() { Comments++; }
        public void IncrementPublications() { Publications++; }
        public void IncrementViews() { Views++; }
        public void IncrementErrors() { Errors++; }

        public double EngagementRate => (Likes + Comments) / (double)Math.Max(1, Views); // Taux d'engagement simple
    }
}