using CardControl.Model.Enums;

namespace CardControl.Model.Responses
{
    public sealed record CardActionResponse(
        string MaskedPan,
        string CorporateId,
        CardStatus Status,
        DateTimeOffset UpdatedAt);
}
