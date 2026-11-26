using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Data.Options
{
    public sealed class DownstreamOptions
    {
        public const string SectionName = "Downstream";
        public string TokenisationBaseUrl { get; set; } = string.Empty;
        public string CardPlatformBaseUrl { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
    }
}
