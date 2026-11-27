using System.ComponentModel.DataAnnotations;

namespace CardControl.Data.Options
{
    public sealed class DownstreamOptions
    {
        public const string SectionName = "Downstream";

        [Required, Url]
        public string TokenisationBaseUrl { get; set; } = string.Empty;

        [Required, Url]
        public string CardPlatformBaseUrl { get; set; } = string.Empty;

        [Required]
        public string Channel { get; set; } = string.Empty;
    }
}
