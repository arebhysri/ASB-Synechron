using System.Net;
using CardControl.Interface;
using CardControl.Model.Downstream;
using CardControl.Model.Enums;
using CardControl.Model.Errors;

namespace CardControl.IntegrationTest.Support
{
    /// <summary>Tokenisation stand-in: same seed PANs/tokens as the starter stub.</summary>
    public sealed class FakeTokenisation : ITokenisationClient
    {
        private static readonly Dictionary<string, string> Tokens = new()
        {
            ["4111111111111111"] = "ctok_8f3a1c2e9b7d4a01",   // CORP-1001, Active
            ["4242424242424242"] = "ctok_2b9e7f4c1a6d3e02",   // CORP-1001, Locked
            ["4012888888881881"] = "ctok_5d1c8a3f7e2b9c03",   // CORP-2002, Active
        };

        public int Calls { get; private set; }

        public Task<string> GetCardTokenAsync(string pan, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Tokens.TryGetValue(pan, out var token) ? token : "ctok_unknown");
        }
    }

    /// <summary>
    /// Card platform stand-in with the starter seed cards. Behaves like the real CardPlatformClient
    /// (throws CardControlException for 404/409) and records every lock/unlock for audit assertions.
    /// </summary>
    public sealed class FakeCardPlatform : ICardPlatformClient
    {
        private readonly Dictionary<string, CardDetails> _cards = new()
        {
            ["ctok_8f3a1c2e9b7d4a01"] = Card("ctok_8f3a1c2e9b7d4a01", "CORP-1001", "411111******1111", CardStatus.Active),
            ["ctok_2b9e7f4c1a6d3e02"] = Card("ctok_2b9e7f4c1a6d3e02", "CORP-1001", "424242******4242", CardStatus.Locked),
            ["ctok_5d1c8a3f7e2b9c03"] = Card("ctok_5d1c8a3f7e2b9c03", "CORP-2002", "401288******1881", CardStatus.Active),
        };

        public List<(string Action, string CardToken, string InitiatingUser)> Audit { get; } = [];

        public Task<CardDetails> GetCardAsync(string cardToken, CancellationToken cancellationToken) =>
            Task.FromResult(Find(cardToken));

        public Task<CardDetails> LockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken) =>
            Task.FromResult(Change(cardToken, "Lock", initiatingUser, CardStatus.Locked));

        public Task<CardDetails> UnlockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken) =>
            Task.FromResult(Change(cardToken, "Unlock", initiatingUser, CardStatus.Active));

        private CardDetails Change(string cardToken, string action, string user, CardStatus target)
        {
            Audit.Add((action, cardToken, user));
            var card = Find(cardToken);
            if (card.Status == target)
                throw target == CardStatus.Locked
                    ? new CardControlException(HttpStatusCode.Conflict, ErrorCodes.CardAlreadyLocked, "The card is already locked.")
                    : new CardControlException(HttpStatusCode.Conflict, ErrorCodes.CardNotLocked, "The card is not locked.");

            var updated = card with { Status = target, UpdatedAt = DateTimeOffset.UtcNow };
            _cards[cardToken] = updated;
            return updated;
        }

        private CardDetails Find(string cardToken) =>
            _cards.TryGetValue(cardToken, out var card)
                ? card
                : throw new CardControlException(HttpStatusCode.NotFound, ErrorCodes.CardNotFound, "Card not found.");

        private static CardDetails Card(string token, string corporate, string masked, CardStatus status) =>
            new(token, corporate, "Test Holder", masked, status, DateTimeOffset.UnixEpoch);
    }
}
