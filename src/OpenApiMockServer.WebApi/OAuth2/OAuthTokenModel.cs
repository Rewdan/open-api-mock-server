using System.Text.Json.Serialization;

namespace OpenApiMockServer.WebApi.OAuth2
{

    /// <summary>
    /// Запрос на получение токена
    /// </summary>
    public class OAuthTokenRequest
    {
        [JsonPropertyName("grant_type")]
        public string? GrantType { get; set; }

        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("client_secret")]
        public string? ClientSecret { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("redirect_uri")]
        public string? RedirectUri { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    /// <summary>
    /// Ответ с токенами
    /// </summary>
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    /// <summary>
    /// Информация о пользователе
    /// </summary>
    public class OAuthUserInfo
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("given_name")]
        public string? GivenName { get; set; }

        [JsonPropertyName("family_name")]
        public string? FamilyName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("email_verified")]
        public bool? EmailVerified { get; set; }

        [JsonPropertyName("preferred_username")]
        public string? PreferredUsername { get; set; }

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }

    /// <summary>
    /// OpenID Connect Discovery конфигурация
    /// </summary>
    public class OAuthWellKnownConfiguration
    {
        [JsonPropertyName("issuer")]
        public string? Issuer { get; set; }

        [JsonPropertyName("authorization_endpoint")]
        public string? AuthorizationEndpoint { get; set; }

        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }

        [JsonPropertyName("userinfo_endpoint")]
        public string? UserinfoEndpoint { get; set; }

        [JsonPropertyName("jwks_uri")]
        public string? JwksUri { get; set; }

        [JsonPropertyName("registration_endpoint")]
        public string? RegistrationEndpoint { get; set; }

        [JsonPropertyName("scopes_supported")]
        public string[]? ScopesSupported { get; set; }

        [JsonPropertyName("response_types_supported")]
        public string[]? ResponseTypesSupported { get; set; }

        [JsonPropertyName("grant_types_supported")]
        public string[]? GrantTypesSupported { get; set; }

        [JsonPropertyName("subject_types_supported")]
        public string[]? SubjectTypesSupported { get; set; }

        [JsonPropertyName("id_token_signing_alg_values_supported")]
        public string[]? IdTokenSigningAlgValuesSupported { get; set; }

        [JsonPropertyName("claims_supported")]
        public string[]? ClaimsSupported { get; set; }
    }

    /// <summary>
    /// JSON Web Key Set
    /// </summary>
    public class OAuthJwks
    {
        [JsonPropertyName("keys")]
        public List<OAuthJwk> Keys { get; set; } = new();
    }

    /// <summary>
    /// JSON Web Key
    /// </summary>
    public class OAuthJwk
    {
        [JsonPropertyName("kty")]
        public string? Kty { get; set; }

        [JsonPropertyName("kid")]
        public string? Kid { get; set; }

        [JsonPropertyName("use")]
        public string? Use { get; set; }

        [JsonPropertyName("alg")]
        public string? Alg { get; set; }

        [JsonPropertyName("n")]
        public string? N { get; set; }

        [JsonPropertyName("e")]
        public string? E { get; set; }
    }

    /// <summary>
    /// Запрос на регистрацию клиента
    /// </summary>
    public class OAuthRegistrationRequest
    {
        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("redirect_uris")]
        public List<string>? RedirectUris { get; set; }

        [JsonPropertyName("grant_types")]
        public List<string>? GrantTypes { get; set; }

        [JsonPropertyName("response_types")]
        public List<string>? ResponseTypes { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("contacts")]
        public List<string>? Contacts { get; set; }

        [JsonPropertyName("tos_uri")]
        public string? TosUri { get; set; }

        [JsonPropertyName("policy_uri")]
        public string? PolicyUri { get; set; }

        [JsonPropertyName("logo_uri")]
        public string? LogoUri { get; set; }
    }

    /// <summary>
    /// Ответ на регистрацию клиента
    /// </summary>
    public class OAuthRegistrationResponse
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("client_secret")]
        public string? ClientSecret { get; set; }

        [JsonPropertyName("client_id_issued_at")]
        public long? ClientIdIssuedAt { get; set; }

        [JsonPropertyName("client_secret_expires_at")]
        public long? ClientSecretExpiresAt { get; set; }

        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("redirect_uris")]
        public List<string>? RedirectUris { get; set; }

        [JsonPropertyName("grant_types")]
        public List<string>? GrantTypes { get; set; }

        [JsonPropertyName("response_types")]
        public List<string>? ResponseTypes { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("contacts")]
        public List<string>? Contacts { get; set; }

        [JsonPropertyName("tos_uri")]
        public string? TosUri { get; set; }

        [JsonPropertyName("policy_uri")]
        public string? PolicyUri { get; set; }

        [JsonPropertyName("logo_uri")]
        public string? LogoUri { get; set; }
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("is_public")]
        public bool IsPublic { get; set; } = false;

    }

    /// <summary>
    /// Клиент OAuth2
    /// </summary>
    public class OAuthClient
    {
        public bool IsPublic { get; set; } = false;
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
        public bool RequiresSecret => !string.IsNullOrEmpty(ClientSecret);
        public List<string> RedirectUris { get; set; } = new();
        public List<string> Scopes { get; set; } = new();
        public List<string> GrantTypes { get; set; } = new();
        public List<string> ResponseTypes { get; set; } = new();
        public string? ClientName { get; set; }
        public List<string>? Contacts { get; set; }
        public string? TosUri { get; set; }
        public string? PolicyUri { get; set; }
        public string? LogoUri { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Ошибка OAuth2
    /// </summary>
    public class OAuthError
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }

        [JsonPropertyName("error_uri")]
        public string? ErrorUri { get; set; }
    }
}
