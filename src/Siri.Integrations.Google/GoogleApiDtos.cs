using System.Text.Json;
using System.Text.Json.Serialization;

namespace Siri.Integrations.Google;

/// <summary>Shared <see cref="JsonSerializerOptions"/> for Google payloads (System.Text.Json only, camelCase, nulls omitted).</summary>
internal static class GoogleJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Body of a successful <c>POST https://oauth2.googleapis.com/token</c> (snake_case on the wire).</summary>
internal sealed record GoogleTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("expires_in")] long? ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("id_token")] string? IdToken = null);
