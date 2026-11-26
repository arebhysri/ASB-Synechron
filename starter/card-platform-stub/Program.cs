// Card Platform Stub
// A fake downstream card platform for the take-home exercise. In-memory, no auth.
// Works by card token only - it never sees a PAN (the tokenisation stub exchanges PANs for tokens).
// It is the system of record for the audit trail: every lock/unlock attempt is recorded with the initiating user.
// Do not treat this as an example of production code - it is deliberately minimal.

using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<CardStore>();
builder.Services.AddSingleton(new FaultSettings(
    builder.Configuration.GetValue("Faults:LatencyMs", 0),
    builder.Configuration.GetValue("Faults:ErrorRate", 0.0)));

var app = builder.Build();

// Fault injection applies to the card endpoints only, so health checks and admin stay reliable.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/cards"), branch => branch.Use(async (ctx, next) =>
{
    var faults = ctx.RequestServices.GetRequiredService<FaultSettings>().Current;

    if (faults.LatencyMs > 0)
        await Task.Delay(faults.LatencyMs, ctx.RequestAborted);

    if (faults.ErrorRate > 0 && Random.Shared.NextDouble() < faults.ErrorRate)
    {
        await Results.Problem(
            title: "Card platform unavailable",
            detail: "Injected fault.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["code"] = "ServiceUnavailable" })
            .ExecuteAsync(ctx);
        return;
    }

    await next();
}));

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// ---- Card endpoints ----

var cards = app.MapGroup("/cards");

cards.MapGet("/{cardToken}", (string cardToken, CardStore store) =>
    store.FindByToken(cardToken) is { } card
        ? Results.Ok(CardResponse.From(card))
        : Error(StatusCodes.Status404NotFound, "CardNotFound", "Unknown card token."));

cards.MapPost("/{cardToken}/lock", (string cardToken, ChangeRequest? request, CardStore store) =>
    Change(cardToken, request, CardAction.Lock, store));

cards.MapPost("/{cardToken}/unlock", (string cardToken, ChangeRequest? request, CardStore store) =>
    Change(cardToken, request, CardAction.Unlock, store));

cards.MapGet("/{cardToken}/audit", (string cardToken, CardStore store) =>
    store.FindByToken(cardToken) is null
        ? Error(StatusCodes.Status404NotFound, "CardNotFound", "Unknown card token.")
        : Results.Ok(store.AuditFor(cardToken)));

// ---- Admin endpoints (for testing / demos only) ----

var admin = app.MapGroup("/admin");

admin.MapGet("/audit", (CardStore store) => store.AllAudit());

admin.MapGet("/faults", (FaultSettings faults) => faults.Current);

admin.MapPut("/faults", (FaultConfig config, FaultSettings faults) =>
{
    if (config.LatencyMs is < 0 or > 120_000 || config.ErrorRate is < 0 or > 1)
        return Error(StatusCodes.Status400BadRequest, "InvalidRequest",
            "latencyMs must be 0-120000 and errorRate must be 0.0-1.0.");

    faults.Current = config;
    return Results.Ok(config);
});

admin.MapPost("/reset", (CardStore store, FaultSettings faults) =>
{
    store.Reset();
    faults.Current = new FaultConfig(0, 0);
    return Results.NoContent();
});

app.Run();

static IResult Change(string cardToken, ChangeRequest? request, CardAction action, CardStore store)
{
    const int maxLength = 128;
    var initiatingUser = request?.InitiatingUser?.Trim();
    var channel = string.IsNullOrWhiteSpace(request?.Channel) ? null : request.Channel.Trim();

    if (string.IsNullOrEmpty(initiatingUser) || initiatingUser.Length > maxLength)
        return Error(StatusCodes.Status400BadRequest, "InvalidRequest", $"'initiatingUser' is required (max {maxLength} characters).");
    if (channel?.Length > maxLength)
        return Error(StatusCodes.Status400BadRequest, "InvalidRequest", $"'channel' must be at most {maxLength} characters.");

    return ToResult(store.Transition(cardToken, action, initiatingUser, channel));
}

static IResult ToResult(TransitionResult result) => result switch
{
    TransitionResult.Ok ok => Results.Ok(CardResponse.From(ok.Card)),
    TransitionResult.NotFound => Error(StatusCodes.Status404NotFound, "CardNotFound", "Unknown card token."),
    TransitionResult.AlreadyInState { Card.Status: CardStatus.Locked } =>
        Error(StatusCodes.Status409Conflict, "CardAlreadyLocked", "The card is already locked."),
    TransitionResult.AlreadyInState =>
        Error(StatusCodes.Status409Conflict, "CardNotLocked", "The card is not locked."),
    TransitionResult.NotPermitted =>
        Error(StatusCodes.Status422UnprocessableEntity, "CardCancelled", "The card is cancelled; no changes are permitted."),
    _ => throw new InvalidOperationException($"Unhandled result {result.GetType().Name}"),
};

static IResult Error(int status, string code, string detail) =>
    Results.Problem(
        title: code,
        detail: detail,
        statusCode: status,
        extensions: new Dictionary<string, object?> { ["code"] = code });

// ---- Model ----

public enum CardStatus { Active, Locked, Cancelled }

public sealed record Card(
    string CardToken,
    string MaskedPan,
    string? CorporateId,
    string CardholderName,
    CardStatus Status,
    DateTimeOffset UpdatedAt);

public enum CardAction { Lock, Unlock }

public sealed record ChangeRequest(string? InitiatingUser, string? Channel);

public sealed record AuditEntry(
    string Id,
    DateTimeOffset At,
    string CardToken,
    CardAction Action,
    string Outcome,
    string InitiatingUser,
    string? Channel);

public sealed record CardResponse(
    string CardToken,
    string? CorporateId,
    string CardholderName,
    string MaskedPan,
    CardStatus Status,
    DateTimeOffset UpdatedAt)
{
    public static CardResponse From(Card c) =>
        new(c.CardToken, c.CorporateId, c.CardholderName, c.MaskedPan, c.Status, c.UpdatedAt);
}

public abstract record TransitionResult
{
    public sealed record Ok(Card Card) : TransitionResult;
    public sealed record NotFound : TransitionResult;
    public sealed record AlreadyInState(Card Card) : TransitionResult;
    public sealed record NotPermitted(Card Card) : TransitionResult;
}

public sealed record FaultConfig(int LatencyMs, double ErrorRate);

public sealed class FaultSettings(int latencyMs, double errorRate)
{
    private volatile FaultConfig _current = new(latencyMs, errorRate);

    public FaultConfig Current
    {
        get => _current;
        set => _current = value;
    }
}

public sealed class CardStore
{
    private readonly Lock _gate = new();
    private Dictionary<string, Card> _byToken = Seed();
    private readonly List<AuditEntry> _audit = [];

    public Card? FindByToken(string cardToken)
    {
        lock (_gate) return _byToken.GetValueOrDefault(cardToken);
    }

    // Every attempt is audited, including rejected ones.
    public TransitionResult Transition(string cardToken, CardAction action, string initiatingUser, string? channel)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var result = Apply(cardToken, action == CardAction.Lock ? CardStatus.Locked : CardStatus.Active, now);

            var outcome = result switch
            {
                TransitionResult.Ok => "Success",
                TransitionResult.NotFound => "CardNotFound",
                TransitionResult.AlreadyInState => action == CardAction.Lock ? "CardAlreadyLocked" : "CardNotLocked",
                TransitionResult.NotPermitted => "CardCancelled",
                _ => throw new InvalidOperationException($"Unhandled result {result.GetType().Name}"),
            };
            _audit.Add(new AuditEntry(Guid.NewGuid().ToString(), now, cardToken, action, outcome, initiatingUser, channel));

            return result;
        }
    }

    public AuditEntry[] AuditFor(string cardToken)
    {
        lock (_gate) return _audit.Where(e => e.CardToken == cardToken).ToArray();
    }

    public AuditEntry[] AllAudit()
    {
        lock (_gate) return [.. _audit];
    }

    public void Reset()
    {
        lock (_gate)
        {
            _byToken = Seed();
            _audit.Clear();
        }
    }

    private TransitionResult Apply(string cardToken, CardStatus target, DateTimeOffset now)
    {
        if (!_byToken.TryGetValue(cardToken, out var card))
            return new TransitionResult.NotFound();

        if (card.Status == CardStatus.Cancelled)
            return new TransitionResult.NotPermitted(card);

        if (card.Status == target)
            return new TransitionResult.AlreadyInState(card);

        var updated = card with { Status = target, UpdatedAt = now };
        _byToken[cardToken] = updated;
        return new TransitionResult.Ok(updated);
    }

    private static Dictionary<string, Card> Seed()
    {
        var seededAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Card[] cards =
        [
            // Tokens must match the tokenisation stub's seed.
            new("ctok_8f3a1c2e9b7d4a01", "411111******1111", "CORP-1001", "Aroha Ngata",  CardStatus.Active,    seededAt),
            new("ctok_2b9e7f4c1a6d3e02", "424242******4242", "CORP-1001", "Liam Chen",    CardStatus.Locked,    seededAt),
            new("ctok_5d1c8a3f7e2b9c03", "401288******1881", "CORP-2002", "Priya Patel",  CardStatus.Active,    seededAt),
            new("ctok_9e4b2d7a1c8f5e04", "400005******5556", "CORP-2002", "Tom Wilson",   CardStatus.Cancelled, seededAt),
            new("ctok_3a7f9c1e5b2d8a05", "400000******3155", "CORP-3003", "Mere Tane",    CardStatus.Active,    seededAt),
            new("ctok_7c2e5a9f3d1b6c06", "400000******3220", null,        "Sam Taylor",   CardStatus.Active,    seededAt),
        ];
        return cards.ToDictionary(c => c.CardToken);
    }
}
