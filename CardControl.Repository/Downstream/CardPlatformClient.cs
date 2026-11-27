using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CardControl.Data.Options;
using CardControl.Interface;
using CardControl.Model.Downstream;
using CardControl.Model.Errors;
using Microsoft.Extensions.Options;

namespace CardControl.Repository.Downstream
{
    public sealed class CardPlatformClient(HttpClient httpClient, IOptions<DownstreamOptions> options) : ICardPlatformClient
    {
        private const string ServiceName = "card platform";

        public async Task<CardDetails> GetCardAsync(string cardToken, CancellationToken cancellationToken)
        {
            using var response = await DownstreamTransport.SendAsync(
                ct => httpClient.GetAsync($"cards/{Uri.EscapeDataString(cardToken)}", ct),
                ServiceName,
                cancellationToken);

            return await ReadCardAsync(response, cancellationToken);
        }

        public Task<CardDetails> LockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken)
            => ChangeAsync(cardToken, "lock", initiatingUser, cancellationToken);

        public Task<CardDetails> UnlockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken)
            => ChangeAsync(cardToken, "unlock", initiatingUser, cancellationToken);

        private async Task<CardDetails> ChangeAsync(string cardToken, string action, string initiatingUser, CancellationToken cancellationToken)
        {
            var body = new CardChangeRequest(initiatingUser, options.Value.Channel);

            using var response = await DownstreamTransport.SendAsync(
                ct => httpClient.PostAsJsonAsync($"cards/{Uri.EscapeDataString(cardToken)}/{action}", body, ct),
                ServiceName,
                cancellationToken);

            return await ReadCardAsync(response, cancellationToken);
        }

        private static async Task<CardDetails> ReadCardAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CardDetails>(cancellationToken)
                    ?? throw new CardControlException(HttpStatusCode.BadGateway, ErrorCodes.CardPlatformFailed,
                        "Card platform returned an empty response.");
            }

            DownstreamProblem? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<DownstreamProblem>(cancellationToken);
            }
            catch (JsonException)
            {
                // Error body is not Problem Details JSON: fall through to the generic 502 below.
            }

            throw problem?.Code switch
            {
                ErrorCodes.CardNotFound => new CardControlException(HttpStatusCode.NotFound, ErrorCodes.CardNotFound, "Card not found."),
                ErrorCodes.CardAlreadyLocked => new CardControlException(HttpStatusCode.Conflict, ErrorCodes.CardAlreadyLocked, "The card is already locked."),
                ErrorCodes.CardNotLocked => new CardControlException(HttpStatusCode.Conflict, ErrorCodes.CardNotLocked, "The card is not locked."),
                ErrorCodes.CardCancelled => new CardControlException(HttpStatusCode.UnprocessableEntity, ErrorCodes.CardCancelled, "The card is cancelled."),
                "ServiceUnavailable" => new CardControlException(HttpStatusCode.ServiceUnavailable, ErrorCodes.CardPlatformUnavailable, "Card platform is temporarily unavailable."),
                _ => new CardControlException(HttpStatusCode.BadGateway, ErrorCodes.CardPlatformFailed, "Card platform request failed."),
            };
        }
    }
}
