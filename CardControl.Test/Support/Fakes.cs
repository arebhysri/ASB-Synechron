using System.Net;
using CardControl.Interface;
using CardControl.Model.Downstream;
using CardControl.Model.Enums;

namespace CardControl.UnitTest.Support
{
    // Small hand-written fakes of the CardControl.Interface contracts (no mocking library, see CLAUDE.md).
    // Each one records how it was called, so tests can assert on it.

    /// <summary>Pretends to decrypt: returns a fixed PAN and counts calls.</summary>
    public sealed class FakePanDecryptor(string pan = "4111111111111111") : IPanDecryptor
    {
        public int Calls { get; private set; }

        public string Decrypt(string encryptedPan)
        {
            Calls++;
            return pan;
        }
    }

    /// <summary>Returns a fixed card token and remembers the PAN it was given.</summary>
    public sealed class FakeTokenisationClient(string cardToken = "ctok_test") : ITokenisationClient
    {
        public string? LastPan { get; private set; }

        public Task<string> GetCardTokenAsync(string pan, CancellationToken cancellationToken)
        {
            LastPan = pan;
            return Task.FromResult(cardToken);
        }
    }

    /// <summary>In-memory card platform: one card, records lock/unlock calls and the initiating user.</summary>
    public sealed class FakeCardPlatformClient(string? corporateId = "CORP-1001", CardStatus status = CardStatus.Active)
        : ICardPlatformClient
    {
        public List<string> Calls { get; } = [];
        public string? LastInitiatingUser { get; private set; }

        private CardDetails Card(CardStatus s) =>
            new("ctok_test", corporateId, "Test Holder", "411111******1111", s, DateTimeOffset.UnixEpoch);

        public Task<CardDetails> GetCardAsync(string cardToken, CancellationToken cancellationToken)
        {
            Calls.Add($"get:{cardToken}");
            return Task.FromResult(Card(status));
        }

        public Task<CardDetails> LockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken)
        {
            Calls.Add($"lock:{cardToken}");
            LastInitiatingUser = initiatingUser;
            return Task.FromResult(Card(CardStatus.Locked));
        }

        public Task<CardDetails> UnlockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken)
        {
            Calls.Add($"unlock:{cardToken}");
            LastInitiatingUser = initiatingUser;
            return Task.FromResult(Card(CardStatus.Active));
        }
    }

    /// <summary>Enabled corporates as a simple list.</summary>
    public sealed class FakeEntitlementService(params string[] enabled) : IEntitlementService
    {
        public bool IsCorporateEnabled(string corporateId) => enabled.Contains(corporateId);
    }

    /// <summary>
    /// Replaces the network for HttpClient: every request goes to <paramref name="respond"/>.
    /// Lets us test the real TokenisationClient / CardPlatformClient without running the stubs.
    /// </summary>
    public sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Read the body now, before HttpClient disposes the request content.
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }

        /// <summary>Builds a response with a JSON body, like the stubs send.</summary>
        public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

        /// <summary>Builds a Problem Details error with a "code", like the stubs send.</summary>
        public static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
            Json(status, $$"""{"title":"{{code}}","status":{{(int)status}},"code":"{{code}}"}""");
    }
}
