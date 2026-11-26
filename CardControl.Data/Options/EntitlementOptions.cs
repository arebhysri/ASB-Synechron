using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Data.Options
{
    public sealed class EntitlementOptions
    {
        public const string SectionName = "Entitlements";
        public List<string> EnabledCorporates { get; set; } = new();
    }
}
