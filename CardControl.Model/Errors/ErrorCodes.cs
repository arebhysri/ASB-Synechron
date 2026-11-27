namespace CardControl.Model.Errors
{
    /// <summary>Machine-readable error codes returned in the Problem Details "code" field.</summary>
    public static class ErrorCodes
    {
        public const string InvalidInitiatingUser = "InvalidInitiatingUser";
        public const string InvalidEncryptedPan = "InvalidEncryptedPan";
        public const string InvalidPan = "InvalidPan";
        public const string CorporateNotEnabled = "CorporateNotEnabled";
        public const string CardNotFound = "CardNotFound";
        public const string CardAlreadyLocked = "CardAlreadyLocked";
        public const string CardNotLocked = "CardNotLocked";
        public const string CardCancelled = "CardCancelled";
        public const string TokenisationFailed = "TokenisationFailed";
        public const string CardPlatformFailed = "CardPlatformFailed";
        public const string CardPlatformUnavailable = "CardPlatformUnavailable";
        public const string DownstreamUnavailable = "DownstreamUnavailable";
        public const string InternalServerError = "InternalServerError";
    }
}
