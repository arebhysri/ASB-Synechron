using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Interface
{
    public interface ITokenisationClient
    {
        Task<string> GetCardTokenAsync(string pan, CancellationToken cancellationToken);
    }
}
