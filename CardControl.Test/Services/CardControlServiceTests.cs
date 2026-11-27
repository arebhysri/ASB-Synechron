using CardControl.Model.Enums;
using CardControl.Model.Errors;
using CardControl.Model.Requests;
using CardControl.Service.CardControl;
using CardControl.UnitTest.Support;

namespace CardControl.UnitTest.Services
{
    public sealed class CardControlServiceTests
    {
        private const string User = "jane.smith@acme.example";

        // Fakes for every dependency: we test only the service's own logic (the main flow).
        private readonly FakePanDecryptor _decryptor = new();
        private readonly FakeTokenisationClient _tokenisation = new();
        private readonly FakeEntitlementService _entitlements = new("CORP-1001", "CORP-2002");

        // Builds the service with a card platform whose single card belongs to cardCorporate.
        private (CardControlService Service, FakeCardPlatformClient Platform) Create(string? cardCorporate = "CORP-1001")
        {
            var platform = new FakeCardPlatformClient(cardCorporate);
            var service = new CardControlService(_decryptor, _tokenisation, platform, _entitlements);
            return (service, platform);
        }

        private static CardActionRequest Request(string corporateId = "CORP-1001") =>
            new() { EncryptedPan = "jwe-from-partner", CorporateId = corporateId };

        [Fact]
        public async Task ExecuteAsync_Lock_LocksCardAndReturnsMaskedPan()
        {
            // Arrange
            var (service, platform) = Create();

            // Act
            var result = await service.ExecuteAsync(CardAction.Lock, Request(), User, CancellationToken.None);

            // Assert: card platform was asked for the card, then to lock it
            Assert.Equal(["get:ctok_test", "lock:ctok_test"], platform.Calls);
            // and the response has the new status and only the masked PAN
            Assert.Equal(CardStatus.Locked, result.Status);
            Assert.Equal("411111******1111", result.MaskedPan);
            Assert.Equal("CORP-1001", result.CorporateId);
        }

        [Fact]
        public async Task ExecuteAsync_Unlock_UnlocksCard()
        {
            var (service, platform) = Create();

            var result = await service.ExecuteAsync(CardAction.Unlock, Request(), User, CancellationToken.None);

            Assert.Equal(["get:ctok_test", "unlock:ctok_test"], platform.Calls);
            Assert.Equal(CardStatus.Active, result.Status);
        }

        [Fact]
        public async Task ExecuteAsync_Lock_PassesInitiatingUserForAudit()
        {
            // Brief requirement 5: the initiating user must reach the card platform on every lock/unlock.
            var (service, platform) = Create();

            await service.ExecuteAsync(CardAction.Lock, Request(), User, CancellationToken.None);

            Assert.Equal(User, platform.LastInitiatingUser);
        }

        [Fact]
        public async Task ExecuteAsync_DecryptedPan_IsSentToTokenisation()
        {
            // The PAN from the decryptor (not the JWE) must be what we tokenise.
            var (service, _) = Create();

            await service.ExecuteAsync(CardAction.Lock, Request(), User, CancellationToken.None);

            Assert.Equal("4111111111111111", _tokenisation.LastPan);
        }

        [Fact]
        public async Task ExecuteAsync_CorporateNotEnabled_Throws403AndNeverTouchesThePan()
        {
            // Arrange: CORP-3003 is not in the enabled list
            var (service, platform) = Create();

            // Act
            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => service.ExecuteAsync(CardAction.Lock, Request("CORP-3003"), User, CancellationToken.None));

            // Assert: 403 CorporateNotEnabled
            Assert.Equal(403, ex.StatusCode);
            Assert.Equal(ErrorCodes.CorporateNotEnabled, ex.Code);
            // and we stopped before decrypting or calling anything downstream
            Assert.Equal(0, _decryptor.Calls);
            Assert.Null(_tokenisation.LastPan);
            Assert.Empty(platform.Calls);
        }

        [Theory]
        [InlineData("CORP-2002")]   // card belongs to another (enabled) corporate
        [InlineData(null)]          // card has no corporate at all
        public async Task ExecuteAsync_CardOfOtherCorporate_Throws404AndDoesNotLock(string? cardCorporate)
        {
            // Partner says CORP-1001, but the card platform says the card is not CORP-1001's.
            var (service, platform) = Create(cardCorporate);

            var ex = await Assert.ThrowsAsync<CardControlException>(
                () => service.ExecuteAsync(CardAction.Lock, Request("CORP-1001"), User, CancellationToken.None));

            // Same 404 as an unknown card, so the partner can't learn about other corporates' cards
            Assert.Equal(404, ex.StatusCode);
            Assert.Equal(ErrorCodes.CardNotFound, ex.Code);
            // and the lock was never sent
            Assert.Equal(["get:ctok_test"], platform.Calls);
        }
    }
}
