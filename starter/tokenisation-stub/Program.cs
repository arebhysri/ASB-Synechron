// Tokenisation Stub
// A fake bank tokenisation API for the take-home exercise: exchanges a PAN for a card token. In-memory, no auth.
// Seeded PANs map to fixed tokens known to the card platform stub. Any other valid PAN gets a new random token,
// stable for the life of the process - the card platform won't recognise it.
// Do not treat this as an example of production code - it is deliberately minimal.

using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<TokenVault>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/tokens", (TokeniseRequest? request, TokenVault vault) =>
{
    var pan = request?.Pan?.Trim();
    if (pan is null || !Luhn.IsValidPan(pan))
        return Results.Problem(
            title: "InvalidPan",
            detail: "'pan' must be 12-19 digits and pass the Luhn check.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = "InvalidPan" });

    return Results.Ok(new TokeniseResponse(vault.Tokenise(pan)));
});

app.MapPost("/admin/reset", (TokenVault vault) =>
{
    vault.Reset();
    return Results.NoContent();
});

app.Run();

public sealed record TokeniseRequest(string? Pan);

public sealed record TokeniseResponse(string CardToken);

public sealed class TokenVault
{
    private readonly Lock _gate = new();
    private Dictionary<string, string> _tokenByPan = Seed();

    public string Tokenise(string pan)
    {
        lock (_gate)
        {
            if (!_tokenByPan.TryGetValue(pan, out var token))
            {
                token = $"ctok_{RandomNumberGenerator.GetHexString(16, lowercase: true)}";
                _tokenByPan[pan] = token;
            }
            return token;
        }
    }

    public void Reset()
    {
        lock (_gate) _tokenByPan = Seed();
    }

    // Tokens must match the card platform stub's seed.
    private static Dictionary<string, string> Seed() => new()
    {
        ["4111111111111111"] = "ctok_8f3a1c2e9b7d4a01",
        ["4242424242424242"] = "ctok_2b9e7f4c1a6d3e02",
        ["4012888888881881"] = "ctok_5d1c8a3f7e2b9c03",
        ["4000056655665556"] = "ctok_9e4b2d7a1c8f5e04",
        ["4000002500003155"] = "ctok_3a7f9c1e5b2d8a05",
        ["4000000000003220"] = "ctok_7c2e5a9f3d1b6c06",
    };
}

public static class Luhn
{
    public static bool IsValidPan(string pan)
    {
        if (pan.Length is < 12 or > 19 || !pan.All(char.IsAsciiDigit))
            return false;

        var sum = 0;
        for (var i = 0; i < pan.Length; i++)
        {
            var digit = pan[pan.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }
}
