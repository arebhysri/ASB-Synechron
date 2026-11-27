using System.ComponentModel.DataAnnotations;

namespace CardControl.Model.Requests
{
    public sealed class CardActionRequest
    {
        // A compact JWE for a PAN is about 700 characters; the limit rejects oversized input before decryption.
        [Required]
        [MaxLength(4096)]
        public string EncryptedPan { get; init; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string CorporateId { get; init; } = string.Empty;
    }
}
