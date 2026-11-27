using System.ComponentModel.DataAnnotations;

namespace CardControl.Data.Options
{
    public sealed class AuthOptions
    {
        public const string SectionName = "Auth";

        /// <summary>Authorization policy that requires <see cref="RequiredRole"/>.</summary>
        public const string PolicyName = "CardControlWrite";

        [Required]
        public string Authority { get; set; } = string.Empty;

        [Required]
        public string Audience { get; set; } = string.Empty;

        [Required]
        public string RequiredRole { get; set; } = string.Empty;
    }
}
