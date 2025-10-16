using System.Text.Json.Serialization;

namespace GeminiDataParsingTestProject.Models
{
    public class ClassMeeting
    {
        [JsonPropertyName("dayOfWeek")]
        public string DayOfWeek { get; set; }

        [JsonPropertyName("startTime")]
        public string StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public string EndTime { get; set; }

        [JsonPropertyName("location")]
        public string Location { get; set; }
    }
}
