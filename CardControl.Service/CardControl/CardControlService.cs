using System.Net;
using CardControl.Interface;
using CardControl.Model.Enums;
using CardControl.Model.Errors;
using CardControl.Model.Requests;
using CardControl.Model.Responses;

namespace CardControl.Service.CardControl
{
    public sealed class CardControlService(
        IPanDecryptor panDecryptor,
        ITokenisationClient tokenisationClient,
        ICardPlatformClient cardPlatformClient,
        IEntitlementService entitlementService) : ICardControlService
    {
        public async Task<CardActionResponse> ExecuteAsync(
            CardAction action,
            CardActionRequest request,
            string initiatingUser,
            CancellationToken cancellationToken)
        {
            // 1. Check the partner is enabled for this corporate (before touching the PAN)
            if (!entitlementService.IsCorporateEnabled(request.CorporateId))
                throw new CardControlException(HttpStatusCode.Forbidden, ErrorCodes.CorporateNotEnabled,
                    "The partner is not enabled for this corporate.");

            // 2. Decrypt the PAN and exchange it for a card token
            var pan = panDecryptor.Decrypt(request.EncryptedPan);
            var cardToken = await tokenisationClient.GetCardTokenAsync(pan, cancellationToken);

            // 3. Get the card and check it belongs to the requested corporate.
            //    Same 404 as an unknown card, so the partner can't learn about other corporates' cards.
            var card = await cardPlatformClient.GetCardAsync(cardToken, cancellationToken);
            if (card.CorporateId is null || !string.Equals(card.CorporateId, request.CorporateId, StringComparison.Ordinal))
                throw new CardControlException(HttpStatusCode.NotFound, ErrorCodes.CardNotFound, "Card not found.");

            // 4. Lock or unlock; the initiating user goes to the card platform's audit trail
            var updated = action == CardAction.Lock
                ? await cardPlatformClient.LockAsync(cardToken, initiatingUser, cancellationToken)
                : await cardPlatformClient.UnlockAsync(cardToken, initiatingUser, cancellationToken);

            return new CardActionResponse(updated.MaskedPan, request.CorporateId, updated.Status, updated.UpdatedAt);
        }
    }
}
