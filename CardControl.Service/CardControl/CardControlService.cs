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
            // 1. Check if the corporate is enabled for this partner
            if (!entitlementService.IsCorporateEnabled(request.CorporateId))
                throw new CardControlException(403, "CorporateNotEnabled", "The partner is not enabled for this corporate.");

            // 2. Decrypt the PAN and get the card token
            var pan = panDecryptor.Decrypt(request.EncryptedPan);
            var cardToken = await tokenisationClient.GetCardTokenAsync(pan, cancellationToken);

            //  3. Get the card details and check if the corporate ID matches
            var card = await cardPlatformClient.GetCardAsync(cardToken, cancellationToken);
            if (card.CorporateId is null || !string.Equals(card.CorporateId, request.CorporateId, StringComparison.Ordinal))
                throw new CardControlException(404, "CardNotFound", "Card not found.");

            // 4. Lock or unlock the card based on the action
            var updated = action == CardAction.Lock
                ? await cardPlatformClient.LockAsync(cardToken, initiatingUser, cancellationToken)
                : await cardPlatformClient.UnlockAsync(cardToken, initiatingUser, cancellationToken);

            return new CardActionResponse(updated.MaskedPan, request.CorporateId, updated.Status, updated.UpdatedAt);
        }
    }
}