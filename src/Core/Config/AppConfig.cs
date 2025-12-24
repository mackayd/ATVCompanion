using System.Text.Json.Serialization;

namespace Core.Config
{
    /// <summary>
    /// Persisted configuration for ATVCompanion.
    /// </summary>
    public class AppConfig
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("mac")]
        public string? Mac { get; set; }

        [JsonPropertyName("device_id")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("auth_key")]
        public string? AuthKey { get; set; }

        // NEW: Persist which brand the user selected in the UI ("Philips" or "Sony")
        [JsonPropertyName("manufacturer")]
        public string? Manufacturer { get; set; }
    }
}
