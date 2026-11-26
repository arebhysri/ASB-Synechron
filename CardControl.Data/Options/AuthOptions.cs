using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Data.Options
{
    public sealed class AuthOptions
    {
        public const string SectionName = "Auth";
        public string Authority { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public string RequiredRole { get; set; } = string.Empty;
    }
}
