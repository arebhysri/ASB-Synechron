using System.Net;
using CardControl.Data.Options;
using CardControl.Model.Enums;
using CardControl.Model.Errors;
using CardControl.Repository.Downstream;
using CardControl.UnitTest.Support;
using Microsoft.Extensions.Options;

namespace CardControl.UnitTest.Repository
{
    public sealed class CardPlatformClientTests
    {
        // A card exactly as the card platform stub returns it (README section 4).
        private const string CardJson = """
            {"cardToken":"ctok_8f3a1c2e9b7d4a01","corporateId":"CORP-1001","cardholderName":"Aroha Ngata",
             "maskedPan":"411111******1111","status":"Locked","updatedAt":"2026-01-01T00:00:00+00:00"}
            """;

        // Real CardPlatformClient + fake network + the channel from DownstreamOptions.
        private static (CardPlatformClient Client, FakeHttpHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var handler = new FakeHttpHandler(respond);
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://cardplatform.test/") };
            var options = Options.Create(new DownstreamOptions { Channel = "partner-app" });
            return (new CardPlatformClient(http, options), handler);
        }

        [Fact]
        public async Task GetCardAsync_Success_ParsesCard()
        {
            var (client, handler) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.OK, CardJson));

            var card = await client.GetCardAsync("ctok_8f3a1c2e9b7d4a01", CancellationToken.None);

            // JSON (camelCase, status as text) is mapped onto our CardDetails record
            Assert.Equal("CORP-1001", card.CorporateId);
            Assert.Equal(CardStatus.Locked, card.Status);
            Assert.Equal("411111******1111", card.MaskedPan);
            // and we called GET /cards/{token}
            Assert.Equal("/cards/ctok_8f3a1c2e9b7d4a01", Assert.Single(handler.Requests).Request.RequestUri!.AbsolutePath);
        }

        [Fact]
        public async Task LockAsync_SendsInitiatingUserAndChannel()
        {
            // Brief requirement 5: every lock carries the initiating user (plus our channel) for the audit trail.
            var (client, handler) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.OK, CardJson));

            await client.LockAsync("ctok_8f3a1c2e9b7d4a01", "jane.smith@acme.example", CancellationToken.None);

            var (request, body) = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/cards/ctok_8f3a1c2e9b7d4a01/lock", request.RequestUri!.AbsolutePath);
            Assert.Contains("\"initiatingUser\":\"jane.smith@acme.example\"", body);
            Assert.Contains("\"channel\":\"partner-app\"", body);
        }

        [Fact]
        public async Task UnlockAsync_CallsUnlockEndpoint()
        {
            var (client, handler) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.OK, CardJson));

            await client.UnlockAsync("ctok_8f3a1c2e9b7d4a01", "jane", CancellationToken.None);

            Assert.Equal("/cards/ctok_8f3a1c2e9b7d4a01/unlock", Assert.Single(handler.Requests).Request.RequestUri!.AbsolutePath);
        }

        // Every error from the stub's README table -> our status + code.
        [Theory]
        [InlineData(HttpStatusCode.NotFound, "CardNotFound", 404, ErrorCodes.CardNotFound)]
        [InlineData(HttpStatusCode.Conflict, "CardAlreadyLocked", 409, ErrorCodes.CardAlreadyLocked)]
        [InlineData(HttpStatusCode.Conflict, "CardNotLocked", 409, ErrorCodes.CardNotLocked)]
        [InlineData(HttpStatusCode.UnprocessableEntity, "CardCancelled", 422, ErrorCodes.CardCancelled)]
        [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable", 503, ErrorCodes.CardPlatformUnavailable)]
        [InlineData(HttpStatusCode.BadRequest, "InvalidRequest", 502, ErrorCodes.CardPlatformFailed)]
        public async Task LockAsync_StubError_MapsToOurError(HttpStatusCode stubStatus, string stubCode, int expectedStatus, string expectedCode)
        {
            var (client, _) = Create(_ => FakeHttpHandler.Problem(stubStatus, stubCode));

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => client.LockAsync("ctok_x", "jane", CancellationToken.None));

            Assert.Equal(expectedStatus, ex.StatusCode);
            Assert.Equal(expectedCode, ex.Code);
        }

        [Fact]
        public async Task GetCardAsync_ErrorBodyNotJson_Throws502()
        {
            // A proxy might send an HTML error page: we can't read a code, so it's a generic 502.
            var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>Bad gateway</html>")
            });

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => client.GetCardAsync("ctok_x", CancellationToken.None));

            Assert.Equal(502, ex.StatusCode);
            Assert.Equal(ErrorCodes.CardPlatformFailed, ex.Code);
        }

        [Fact]
        public async Task GetCardAsync_StubUnreachable_Throws503()
        {
            var (client, _) = Create(_ => throw new HttpRequestException("Connection refused"));

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => client.GetCardAsync("ctok_x", CancellationToken.None));

            Assert.Equal(503, ex.StatusCode);
            Assert.Equal(ErrorCodes.DownstreamUnavailable, ex.Code);
        }

        [Fact]
        public async Task GetCardAsync_HttpClientTimeout_Throws503()
        {
            // HttpClient.Timeout shows up as TaskCanceledException while OUR token is not cancelled.
            var (client, _) = Create(_ => throw new TaskCanceledException("timeout"));

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => client.GetCardAsync("ctok_x", CancellationToken.None));

            Assert.Equal(503, ex.StatusCode);
        }
    }
}
