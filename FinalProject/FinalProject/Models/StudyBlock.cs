using System.Text.Json.Serialization;

namespace FinalProject.Models
{
    public class StudyBlock
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("startDate")]
        public string? StartDateString { get; set; }

        [JsonPropertyName("endDate")]
        public string? EndDateString { get; set; }

        [JsonIgnore]
        public DateTime StartDate
        {
            get => DateTime.TryParse(StartDateString, out var dt) ? dt : DateTime.Now;
            set => StartDateString = value.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        [JsonIgnore]
        public DateTime EndDate
        {
            get => DateTime.TryParse(EndDateString, out var dt) ? dt : DateTime.Now;
            set => EndDateString = value.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        [JsonPropertyName("eventType")]
        public string? EventType { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
