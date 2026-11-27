using System.Net;
using System.Net.Http.Json;
using CardControl.Interface;
using CardControl.Model.Downstream;
using CardControl.Model.Errors;

namespace CardControl.Repository.Downstream
{
    public sealed class TokenisationClient(HttpClient httpClient) : ITokenisationClient
    {
        private const string ServiceName = "tokenisation service";

        public async Task<string> GetCardTokenAsync(string pan, CancellationToken cancellationToken)
        {
            using var response = await DownstreamTransport.SendAsync(
                ct => httpClient.PostAsJsonAsync("tokens", new TokeniseRequest(pan), ct),
                ServiceName,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<TokeniseResponse>(cancellationToken);
                return body?.CardToken
                    ?? throw new CardControlException(HttpStatusCode.BadGateway, ErrorCodes.TokenisationFailed,
                        "Tokenisation service returned an empty response.");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new CardControlException(HttpStatusCode.BadRequest, ErrorCodes.InvalidPan,
                    "The card number is not valid.");

            throw new CardControlException(HttpStatusCode.BadGateway, ErrorCodes.TokenisationFailed,
                "Tokenisation service failed.");
        }
    }
}
