using CardControl.Data.Options;
using CardControl.Interface;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Repository.Entitlements
{
    public sealed class EntitlementService(IOptions<EntitlementOptions> options) : IEntitlementService
    {
        public bool IsCorporateEnabled(string corporateId) => _enabled.Contains(corporateId);
        private readonly HashSet<string> _enabled = new(options.Value.EnabledCorporates, StringComparer.Ordinal);
    }
}
