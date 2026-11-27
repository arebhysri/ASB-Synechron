using CardControl.Model.Enums;

namespace CardControl.Model.Downstream
{
    public sealed record CardDetails(
        string CardToken,
        string? CorporateId,
        string CardholderName,
        string MaskedPan,
        CardStatus Status,
        DateTimeOffset UpdatedAt);
}
