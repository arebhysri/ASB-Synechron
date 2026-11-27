using CardControl.Model.Downstream;

namespace CardControl.Interface
{
    public interface ICardPlatformClient
    {
        Task<CardDetails> GetCardAsync(string cardToken, CancellationToken cancellationToken);

        Task<CardDetails> LockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken);

        Task<CardDetails> UnlockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken);
    }
}
