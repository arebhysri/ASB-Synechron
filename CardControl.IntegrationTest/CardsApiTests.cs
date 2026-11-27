using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CardControl.IntegrationTest.Support;
using CardControl.Model.Errors;
using Microsoft.IdentityModel.Tokens;

namespace CardControl.IntegrationTest
{
    /// <summary>
    /// End-to-end through the real HTTP pipeline: routing, JWT auth, controller, service, JWE decryption,
    /// entitlement and the global exception handler. Only the two downstream services are faked.
    /// A new factory per test (xUnit creates a new class instance per test) keeps card state isolated.
    /// </summary>
    public sealed class CardsApiTests : IDisposable
    {
        private const string Pan = "4111111111111111";          // seed card: CORP-1001, Active
        private const string LockedPan = "4242424242424242";    // seed card: CORP-1001, Locked
        private const string OtherCorpPan = "4012888888881881"; // seed card: CORP-2002, Active
        private const string User = "jane.smith@acme.example";

        private readonly CardControlApiFactory _factory = new();
        private readonly HttpClient _client;

        public CardsApiTests() => _client = _factory.CreateClient();

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
        }

        // ---------- Happy paths ----------

        [Fact]
        public async Task Lock_ValidRequest_Returns200WithMaskedPanOnly()
        {
            // Act: a correct partner request
            var response = await SendAsync("lock", Pan, "CORP-1001");

            // Assert: 200 with the locked card
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Locked", json.GetProperty("status").GetString());
            Assert.Equal("411111******1111", json.GetProperty("maskedPan").GetString());
            Assert.Equal("CORP-1001", json.GetProperty("corporateId").GetString());
            // and the response never exposes the card token
            Assert.False(json.TryGetProperty("cardToken", out _));
        }

        [Fact]
        public async Task Unlock_LockedCard_Returns200AndRecordsInitiatingUser()
        {
            var response = await SendAsync("unlock", LockedPan, "CORP-1001");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Audit (brief requirement 5): the card platform got the X-Initiating-User value
            var audit = Assert.Single(_factory.CardPlatform.Audit);
            Assert.Equal(("Unlock", "ctok_2b9e7f4c1a6d3e02", User), audit);
        }

        // ---------- Authentication (401) and authorization (403) ----------

        [Fact]
        public async Task Lock_NoToken_Returns401()
        {
            var response = await SendAsync("lock", Pan, "CORP-1001", token: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Lock_ExpiredToken_Returns401()
        {
            // Expired 5 minutes ago: beyond the 30 s clock skew we allow
            var token = _factory.Keys.CreateToken([TestKeys.Role], expires: DateTime.UtcNow.AddMinutes(-5));

            var response = await SendAsync("lock", Pan, "CORP-1001", token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Lock_TokenForAnotherAudience_Returns401()
        {
            // Valid token, but issued for a different API (like scope=api://other-api/.default)
            var token = _factory.Keys.CreateToken([TestKeys.Role], audience: "api://other-api");

            var response = await SendAsync("lock", Pan, "CORP-1001", token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Lock_TokenSignedByUnknownKey_Returns401()
        {
            // Someone forged a token with their own key: signature check must fail
            using var attackerKey = System.Security.Cryptography.RSA.Create(2048);
            var token = _factory.Keys.CreateToken([TestKeys.Role], signWith: new RsaSecurityKey(attackerKey));

            var response = await SendAsync("lock", Pan, "CORP-1001", token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Lock_TokenWithoutRole_Returns403AndDoesNothing()
        {
            // Like the partner-readonly client: authenticated, but no CardControl.Write role
            var token = _factory.Keys.CreateToken(roles: null);

            var response = await SendAsync("lock", Pan, "CORP-1001", token);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(_factory.CardPlatform.Audit);
        }

        // ---------- Input validation (400) ----------

        [Fact]
        public async Task Lock_MissingInitiatingUser_Returns400()
        {
            var response = await SendAsync("lock", Pan, "CORP-1001", initiatingUser: null);

            await AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidInitiatingUser);
        }

        [Fact]
        public async Task Lock_InitiatingUserTooLong_Returns400()
        {
            // Card platform accepts max 128 characters, so we reject longer values up front
            var response = await SendAsync("lock", Pan, "CORP-1001", initiatingUser: new string('a', 129));

            await AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidInitiatingUser);
        }

        [Fact]
        public async Task Lock_InvalidJwe_Returns400()
        {
            var response = await SendRawAsync("lock", encryptedPan: "not.a.valid.jwe.value", corporateId: "CORP-1001");

            await AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidEncryptedPan);
        }

        [Fact]
        public async Task Lock_MissingBodyFields_Returns400ValidationProblem()
        {
            // [Required] on CardActionRequest + [ApiController] -> automatic 400 Problem Details
            using var request = NewRequest("lock", _factory.Keys.CreateToken([TestKeys.Role]), User);
            request.Content = JsonContent.Create(new { });

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        // ---------- Entitlement (403 / 404) ----------

        [Fact]
        public async Task Lock_CorporateNotEnabled_Returns403BeforeCallingDownstream()
        {
            var response = await SendAsync("lock", Pan, "CORP-3003");

            await AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.CorporateNotEnabled);
            // Checked before decrypting / tokenising: no downstream call at all
            Assert.Equal(0, _factory.Tokenisation.Calls);
            Assert.Empty(_factory.CardPlatform.Audit);
        }

        [Fact]
        public async Task Lock_CardBelongsToOtherCorporate_Returns404AndDoesNotLock()
        {
            // Partner claims CORP-1001, but the card is CORP-2002's (both are enabled)
            var response = await SendAsync("lock", OtherCorpPan, "CORP-1001");

            await AssertProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.CardNotFound);
            Assert.Empty(_factory.CardPlatform.Audit);
        }

        // ---------- Card state (409) ----------

        [Fact]
        public async Task Lock_AlreadyLockedCard_Returns409()
        {
            var response = await SendAsync("lock", LockedPan, "CORP-1001");

            await AssertProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.CardAlreadyLocked);
        }

        // ---------- PAN must never leak ----------

        [Fact]
        public async Task AnyRequest_PanNeverAppearsInResponsesOrLogs()
        {
            // Arrange: a mix of success and failure paths that all handle the PAN
            var responses = new List<HttpResponseMessage>
            {
                await SendAsync("lock", Pan, "CORP-1001"),       // 200
                await SendAsync("lock", Pan, "CORP-1001"),       // 409
                await SendAsync("unlock", OtherCorpPan, "CORP-1001"), // 404
            };

            // Assert: no response body contains the raw PAN digits
            foreach (var response in responses)
                Assert.DoesNotContain(Pan, await response.Content.ReadAsStringAsync());

            // and no log line (at any level, including Trace) contains any of the PANs
            foreach (var line in _factory.Logs.Lines)
            {
                Assert.DoesNotContain(Pan, line);
                Assert.DoesNotContain(OtherCorpPan, line);
            }
        }

        // ---------- Helpers ----------

        // Sends a request the way the partner does: Bearer token + X-Initiating-User + JSON body with an encrypted PAN.
        // Passing token: null or initiatingUser: null leaves that header out.
        private Task<HttpResponseMessage> SendAsync(string action, string pan, string corporateId,
            string? token = "default", string? initiatingUser = User)
        {
            if (token == "default")
                token = _factory.Keys.CreateToken([TestKeys.Role]);

            return SendRawAsync(action, _factory.Keys.EncryptPan(pan), corporateId, token, initiatingUser);
        }

        private Task<HttpResponseMessage> SendRawAsync(string action, string encryptedPan, string corporateId,
            string? token = "default", string? initiatingUser = User)
        {
            if (token == "default")
                token = _factory.Keys.CreateToken([TestKeys.Role]);

            var request = NewRequest(action, token, initiatingUser);
            request.Content = JsonContent.Create(new { encryptedPan, corporateId });
            return _client.SendAsync(request);
        }

        private static HttpRequestMessage NewRequest(string action, string? token, string? initiatingUser)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/cards/{action}");
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (initiatingUser is not null)
                request.Headers.Add("X-Initiating-User", initiatingUser);
            return request;
        }

        // Every error must be RFC 9457 Problem Details with our machine-readable "code".
        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal((int)status, json.GetProperty("status").GetInt32());
            Assert.Equal(code, json.GetProperty("code").GetString());
        }
    }
}
