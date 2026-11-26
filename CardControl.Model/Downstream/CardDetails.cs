using CardControl.Model.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Model.Downstream
{
    public sealed record CardDetails(string CardToken, string? CorporateId, string CardholderName, string MaskedPan, CardStatus Status, DateTimeOffset UpdatedAt);
}
