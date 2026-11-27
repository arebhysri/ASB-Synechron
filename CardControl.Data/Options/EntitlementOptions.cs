using System.ComponentModel.DataAnnotations;

namespace CardControl.Data.Options
{
    public sealed class EntitlementOptions
    {
        public const string SectionName = "Entitlements";

        [MinLength(1)]
        public List<string> EnabledCorporates { get; set; } = new();
    }
}
