using CardControl.Model.Downstream;
using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Interface
{
    public interface ICardPlatformClient
    {
        Task<CardDetails> GetCardAsync(string cardToken, CancellationToken cancellationToken);
        Task<CardDetails> LockAsync(string cardToken,string initiatingUser, CancellationToken cancellationToken);
        Task<CardDetails> UnlockAsync(string cardToken, string initiatingUser, CancellationToken cancellationToken);
    }
}
