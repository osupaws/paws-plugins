using System.Text.Json.Serialization;

namespace MyVuePlugin.Models
{
    public class MyOptions
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "default";

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; set; } = false;
    }
}
