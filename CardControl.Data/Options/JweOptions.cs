using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Data.Options
{
    public sealed class JweOptions
    {
        public const string SectionName = "Jwe";
        public string PrivateKeyPath { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
    }
}
