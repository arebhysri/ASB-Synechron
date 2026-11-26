// Mock Identity Provider
// A fake OAuth 2.0 / OpenID Connect authorisation server for the take-home exercise, loosely modelled on
// Entra ID. Client credentials grant only. The signing key is generated at start-up and kept in memory,
// so tokens from a previous run stop validating after a restart.
// Do not treat this as an example of production code - it is deliberately minimal.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<IdpOptions>(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenIssuer>();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<IdpOptions>>().Value;
var issuer = options.NormalisedIssuer;

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/.well-known/openid-configuration", () => Results.Ok(new
{
    issuer,
    token_endpoint = $"{issuer}/oauth2/token",
    jwks_uri = $"{issuer}/jwks",
    grant_types_supported = new[] { "client_credentials" },
    token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
    response_types_supported = new[] { "token" },
    subject_types_supported = new[] { "public" },
    id_token_signing_alg_values_supported = new[] { "RS256" },
}));

app.MapGet("/jwks", (TokenIssuer tokens) => Results.Ok(new { keys = new[] { tokens.PublicJwk } }));

app.MapPost("/oauth2/token", async (HttpContext ctx, TokenIssuer tokens, ILogger<Program> logger) =>
{
    var request = ctx.Request;
    ctx.Response.Headers.CacheControl = "no-store";

    if (!request.HasFormContentType)
        return OAuthError(ctx, "invalid_request", "Content-Type must be application/x-www-form-urlencoded.");

    var form = await request.ReadFormAsync(ctx.RequestAborted);

    // Client authentication: HTTP Basic (client_secret_basic) or form fields (client_secret_post).
    var (clientId, clientSecret, usedBasic) = ReadClientCredentials(request, form);
    var client = options.Clients.FirstOrDefault(c => c.ClientId == clientId);
    if (client is null || clientSecret is null || !FixedTimeEquals(client.ClientSecret, clientSecret))
        return OAuthError(ctx, "invalid_client", "Client authentication failed.", StatusCodes.Status401Unauthorized, usedBasic);

    if (form["grant_type"] != "client_credentials")
        return OAuthError(ctx, "unsupported_grant_type", "Only 'client_credentials' is supported.");

    // Entra-style scope: "<resource>/.default". The resource becomes the token audience.
    const string defaultSuffix = "/.default";
    var scope = form["scope"].ToString();
    if (!scope.EndsWith(defaultSuffix, StringComparison.Ordinal))
        return OAuthError(ctx, "invalid_scope", $"Scope must be '<resource>{defaultSuffix}'.");

    var audience = scope[..^defaultSuffix.Length];
    if (!options.Resources.Contains(audience, StringComparer.Ordinal))
        return OAuthError(ctx, "invalid_scope", $"Unknown resource '{audience}'.");

    // Mock-only extension for testing: lifetime_seconds, 1..3600, or -3600..-1 for an already-expired token.
    var lifetime = options.TokenLifetimeSeconds;
    if (!StringValues.IsNullOrEmpty(form["lifetime_seconds"]))
    {
        if (!int.TryParse(form["lifetime_seconds"], out lifetime) || lifetime == 0 || lifetime is < -3600 or > 3600)
            return OAuthError(ctx, "invalid_request", "lifetime_seconds must be 1..3600, or -3600..-1 for an expired token.");
    }

    var token = tokens.Issue(client, audience, lifetime);

    logger.LogInformation(
        "Issued token to {ClientId} for {Audience} with roles [{Roles}], lifetime {Lifetime}s",
        client.ClientId, audience, string.Join(", ", client.Roles), lifetime);

    return Results.Ok(new { access_token = token, token_type = "Bearer", expires_in = Math.Max(lifetime, 0) });
});

app.Run();

static (string? ClientId, string? ClientSecret, bool UsedBasic) ReadClientCredentials(HttpRequest request, IFormCollection form)
{
    var authorization = request.Headers.Authorization.ToString();
    if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..].Trim()));
            var separator = decoded.IndexOf(':');
            if (separator > 0)
                return (Uri.UnescapeDataString(decoded[..separator]), Uri.UnescapeDataString(decoded[(separator + 1)..]), true);
        }
        catch (FormatException)
        {
            // Fall through: treated as failed client authentication.
        }
        return (null, null, true);
    }

    return (form["client_id"].ToString(), form["client_secret"].ToString(), false);
}

static bool FixedTimeEquals(string expected, string actual) =>
    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

static IResult OAuthError(HttpContext ctx, string error, string description,
    int status = StatusCodes.Status400BadRequest, bool basicChallenge = false)
{
    if (basicChallenge)
        ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"mock-identity-provider\"";

    return Results.Json(new { error, error_description = description }, statusCode: status);
}

// ---- Options ----

public sealed class IdpOptions
{
    public string Issuer { get; set; } = "http://localhost:5090";
    public string TenantId { get; set; } = "";
    public int TokenLifetimeSeconds { get; set; } = 3600;
    public string[] Resources { get; set; } = [];
    public ClientOptions[] Clients { get; set; } = [];

    public string NormalisedIssuer => Issuer.TrimEnd('/');
}

public sealed class ClientOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string[] Roles { get; set; } = [];
}

// ---- Token issuance ----

public sealed class TokenIssuer : IDisposable
{
    private readonly RSA _signingKey = RSA.Create(2048);
    private readonly Lock _signingGate = new();
    private readonly IdpOptions _options;
    private readonly TimeProvider _time;
    private readonly string _keyId;

    public TokenIssuer(IOptions<IdpOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;

        var parameters = _signingKey.ExportParameters(includePrivateParameters: false);
        _keyId = Base64Url.EncodeToString(SHA256.HashData(parameters.Modulus!))[..16];
        PublicJwk = new Jwk("RSA", "sig", "RS256", _keyId,
            Base64Url.EncodeToString(parameters.Modulus!),
            Base64Url.EncodeToString(parameters.Exponent!));
    }

    public Jwk PublicJwk { get; }

    public string Issue(ClientOptions client, string audience, int lifetimeSeconds)
    {
        var now = _time.GetUtcNow();
        var expires = now.AddSeconds(lifetimeSeconds);
        var issuedAt = lifetimeSeconds > 0 ? now : expires.AddHours(-1);

        var header = WriteJson(w =>
        {
            w.WriteString("alg", "RS256");
            w.WriteString("kid", _keyId);
            w.WriteString("typ", "JWT");
        });

        var payload = WriteJson(w =>
        {
            w.WriteString("iss", _options.NormalisedIssuer);
            w.WriteString("aud", audience);
            w.WriteString("sub", client.ObjectId);
            w.WriteString("azp", client.ClientId);
            w.WriteString("azpacr", "1");
            w.WriteString("tid", _options.TenantId);
            if (client.Roles.Length > 0)
            {
                w.WriteStartArray("roles");
                foreach (var role in client.Roles)
                    w.WriteStringValue(role);
                w.WriteEndArray();
            }
            w.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
            w.WriteNumber("nbf", issuedAt.ToUnixTimeSeconds());
            w.WriteNumber("exp", expires.ToUnixTimeSeconds());
            w.WriteString("jti", Guid.NewGuid().ToString());
            w.WriteString("ver", "2.0");
        });

        var signingInput = $"{Base64Url.EncodeToString(header)}.{Base64Url.EncodeToString(payload)}";

        byte[] signature;
        lock (_signingGate)
            signature = _signingKey.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    public void Dispose() => _signingKey.Dispose();

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}

public sealed record Jwk(string kty, string use, string alg, string kid, string n, string e);
