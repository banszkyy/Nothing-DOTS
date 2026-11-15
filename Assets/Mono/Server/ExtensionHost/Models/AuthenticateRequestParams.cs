using Newtonsoft.Json;

public class AuthenticateRequestParams
{
    [JsonProperty("token", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public required string Token { get; set; }
}
