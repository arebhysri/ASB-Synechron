using System.ComponentModel.DataAnnotations;

namespace CardControl.Data.Options
{
    public sealed class JweOptions
    {
        public const string SectionName = "Jwe";

        [Required]
        public string PrivateKeyPath { get; set; } = string.Empty;

        [Required]
        public string KeyId { get; set; } = string.Empty;
    }
}
