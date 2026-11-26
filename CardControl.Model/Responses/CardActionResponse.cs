using CardControl.Model.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Model.Responses
{
    public sealed record CardActionResponse(
        string MaskedPan,
        string CorporateId,
        CardStatus Status,
        DateTimeOffset UpdatedAt);
}
