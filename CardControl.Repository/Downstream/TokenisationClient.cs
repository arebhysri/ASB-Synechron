using CardControl.Interface;
using CardControl.Model.Downstream;
using CardControl.Model.Errors;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace CardControl.Repository.Downstream
{
    public sealed class TokenisationClient(HttpClient httpClient) : ITokenisationClient
    {
        public async Task<string> GetCardTokenAsync(string pan, CancellationToken cancellationToken)
        {
            using var response = await httpClient.PostAsJsonAsync("tokens", new TokeniseRequest(pan), cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<TokeniseResponse>(cancellationToken);
                return body?.CardToken ?? throw new CardControlException(502, "TokenisationFailed", "Tokenisation service returned an empty response");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new CardControlException(400, "InvalidPan", "The card number is not valid.");
            }

            throw new CardControlException(502, "TokenisationFailed", "Tokenisation service failed.");
        }
    }
}
