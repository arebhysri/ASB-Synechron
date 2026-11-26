using CardControl.Model.Enums;
using CardControl.Model.Requests;
using CardControl.Model.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Interface
{
    public interface ICardControlService
    {
        Task<CardActionResponse> ExecuteAsync(CardAction action, CardActionRequest request, string initiatingUser, CancellationToken cancellationToken);
    }
}
