using CardControl.Data.Options;
using CardControl.Interface;
using Microsoft.Extensions.Options;

namespace CardControl.Repository.Entitlements
{
    /// <summary>Corporates this partner may act on, from appsettings.json (Entitlements:EnabledCorporates).</summary>
    public sealed class EntitlementService(IOptions<EntitlementOptions> options) : IEntitlementService
    {
        private readonly HashSet<string> _enabled = new(options.Value.EnabledCorporates, StringComparer.Ordinal);

        public bool IsCorporateEnabled(string corporateId) => _enabled.Contains(corporateId);
    }
}
