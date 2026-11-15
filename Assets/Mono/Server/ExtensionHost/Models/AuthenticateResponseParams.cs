using Newtonsoft.Json;

public class AuthenticateResponseParams
{
    [JsonProperty("success")]
    public required bool Success { get; set; }
}
