using CardControl.Data.Options;
using CardControl.Interface;
using CardControl.Model.Downstream;
using CardControl.Model.Errors;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

namespace CardControl.Repository.Downstream
{
    public sealed class CardPlatformClient(HttpClient httpClient, IOptions<DownstreamOptions> options) : ICardPlatformClient
    {
        public async Task<CardDetails> GetCardAsync(string cardToken, CancellationToken cancellationToken)
        {
            using var response = await httpClient.GetAsync($"cards/{Uri.EscapeDataString(cardToken)}", cancellationToken);
            return await ReadCardAsync(response, cancellationToken);
        }

        public Task<CardDetails> LockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken) => ChangeAsync(cardToken, "lock", initiatingUser, cancellationToken);
        

        public Task<CardDetails> UnlockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken)
        => ChangeAsync(cardToken, "unlock", initiatingUser, cancellationToken);

        private async Task<CardDetails> ChangeAsync(string cardToken, string action, string initiatingUser, CancellationToken cancellationToken)
        {
            var body = new CardChangeRequest(initiatingUser, options.Value.Channel);
            using var response = await httpClient.PostAsJsonAsync($"cards/{Uri.EscapeDataString(cardToken)}/{action}", body, cancellationToken);
            return await ReadCardAsync(response, cancellationToken);
        }

        private static async Task<CardDetails> ReadCardAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CardDetails>(cancellationToken) ?? throw new CardControlException(502, "CardPlatformFailed", "Card platform returned an empty response.");
            }

            DownstreamProblem? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<DownstreamProblem>(cancellationToken);
            }
            catch { }
            throw problem?.Code switch
            {
                "CardNotFound" => new CardControlException(404, "CardNotFound", "Card not found."),
                "CardAlreadyLocked" => new CardControlException(409, "CardAlreadyLocked", "The card is already locked."),
                "CardNotLocked" => new CardControlException(409, "CardNotLocked", "The card is not locked."),
                "CardCancelled" => new CardControlException(422, "CardCancelled", "The card is cancelled."),
                "ServiceUnavailable" => new CardControlException(503, "CardPlatformUnavailable", "Card platform is temporarily unavailable."),
                _ => new CardControlException(502, "CardPlatformFailed", "Card platform request failed."),
            };

        }
    }
}
