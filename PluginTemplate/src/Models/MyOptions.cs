using System.Text.Json.Serialization;

namespace MyPlugin.Models
{
    public class MyOptions
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "default";

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; set; } = false;
    }
}
