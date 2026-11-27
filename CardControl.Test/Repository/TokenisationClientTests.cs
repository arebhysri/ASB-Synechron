using System.Net;
using CardControl.Model.Errors;
using CardControl.Repository.Downstream;
using CardControl.UnitTest.Support;

namespace CardControl.UnitTest.Repository
{
    public sealed class TokenisationClientTests
    {
        // Real TokenisationClient, but its HttpClient talks to a FakeHttpHandler instead of the network.
        private static (TokenisationClient Client, FakeHttpHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var handler = new FakeHttpHandler(respond);
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://tokenisation.test/") };
            return (new TokenisationClient(http), handler);
        }

        [Fact]
        public async Task GetCardTokenAsync_Success_PostsPanAndReturnsToken()
        {
            // Arrange: stub answers 200 with a token (README section 3)
            var (client, handler) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.OK, """{"cardToken":"ctok_123"}"""));

            // Act
            var token = await client.GetCardTokenAsync("4111111111111111", CancellationToken.None);

            // Assert: we got the token
            Assert.Equal("ctok_123", token);
            // and sent POST /tokens with {"pan":"..."}
            var (request, body) = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/tokens", request.RequestUri!.AbsolutePath);
            Assert.Contains("\"pan\":\"4111111111111111\"", body);
        }

        [Fact]
        public async Task GetCardTokenAsync_400FromStub_ThrowsInvalidPan()
        {
            // Stub says the PAN fails Luhn / length -> our 400 InvalidPan
            var (client, _) = Create(_ => FakeHttpHandler.Problem(HttpStatusCode.BadRequest, "InvalidPan"));

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => client.GetCardTokenAsync("4111111111111112", CancellationToken.None));

            Assert.Equal(400, ex.StatusCode);
            Assert.Equal(ErrorCodes.InvalidPan, ex.Code);
        }

        [Fact]
        public async Task GetCardTokenAsync_500FromStub_Throws502()
        {
            // Unexpected downstream error -> 502 Bad Gateway (it's their fault, not the partner's)
            var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => client.GetCardTokenAsync("4111111111111111", CancellationToken.None));

            Assert.Equal(502, ex.StatusCode);
            Assert.Equal(ErrorCodes.TokenisationFailed, ex.Code);
        }

        [Fact]
        public async Task GetCardTokenAsync_StubUnreachable_Throws503()
        {
            // Simulate "connection refused": the handler throws like HttpClient does when the stub is down
            var (client, _) = Create(_ => throw new HttpRequestException("Connection refused"));

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => client.GetCardTokenAsync("4111111111111111", CancellationToken.None));

            Assert.Equal(503, ex.StatusCode);
            Assert.Equal(ErrorCodes.DownstreamUnavailable, ex.Code);
        }

        [Fact]
        public async Task GetCardTokenAsync_CallerCancels_DoesNotBecome503()
        {
            // If the partner hangs up (our token is cancelled) it's not a downstream failure:
            // the cancellation must propagate as-is, not be reported as 503.
            using var cts = new CancellationTokenSource();
            var (client, _) = Create(_ =>
            {
                cts.Cancel();
                throw new TaskCanceledException();
            });

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => client.GetCardTokenAsync("4111111111111111", cts.Token));
        }
    }
}
