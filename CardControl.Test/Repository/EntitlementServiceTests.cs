using CardControl.Data.Options;
using CardControl.Repository.Entitlements;
using Microsoft.Extensions.Options;

namespace CardControl.UnitTest.Repository
{
    public sealed class EntitlementServiceTests
    {
        // Same list as appsettings.json: the partner is enabled for CORP-1001 and CORP-2002 only (brief requirement 3).
        private readonly EntitlementService _service = new(
            Options.Create(new EntitlementOptions { EnabledCorporates = ["CORP-1001", "CORP-2002"] }));

        [Theory]
        [InlineData("CORP-1001")]
        [InlineData("CORP-2002")]
        public void IsCorporateEnabled_EnabledCorporate_ReturnsTrue(string corporateId)
        {
            Assert.True(_service.IsCorporateEnabled(corporateId));
        }

        [Theory]
        [InlineData("CORP-3003")]   // real corporate, but not enabled for this partner
        [InlineData("corp-1001")]   // comparison is exact (ordinal), no case tricks
        [InlineData(" CORP-1001")]  // no trimming either
        [InlineData("")]
        public void IsCorporateEnabled_OtherValue_ReturnsFalse(string corporateId)
        {
            Assert.False(_service.IsCorporateEnabled(corporateId));
        }
    }
}
