using CardControl.Data.Options;
using CardControl.Model.Errors;
using CardControl.Service.Security;
using CardControl.UnitTest.Support;
using Microsoft.Extensions.Options;

namespace CardControl.UnitTest.Security
{
    public sealed class JwePanDecryptorTests : IDisposable
    {
        private const string Pan = "4111111111111111";

        // TestJwe = the partner side (encrypts); _decryptor = our API side (decrypts) with the matching private key.
        private readonly TestJwe _jwe = new();
        private readonly JwePanDecryptor _decryptor;

        public JwePanDecryptorTests()
        {
            // Build the real decryptor with options pointing at the test key file.
            var options = Options.Create(new JweOptions { PrivateKeyPath = _jwe.PrivateKeyPath, KeyId = TestJwe.KeyId });
            _decryptor = new JwePanDecryptor(options);
        }

        public void Dispose()
        {
            _decryptor.Dispose();
            _jwe.Dispose();
        }

        [Fact]
        public void Decrypt_ValidJwe_ReturnsPan()
        {
            // Arrange: encrypt the PAN exactly like the partner does
            var encrypted = _jwe.Encrypt(Pan);

            // Act: our API decrypts it
            var result = _decryptor.Decrypt(encrypted);

            // Assert: we get the original digits back
            Assert.Equal(Pan, result);
        }

        [Fact]
        public void Decrypt_WrongKeyId_ThrowsInvalidEncryptedPan()
        {
            // A JWE for a key we don't hold must be rejected.
            var encrypted = _jwe.Encrypt(Pan, kid: "some-other-key");

            AssertInvalid(() => _decryptor.Decrypt(encrypted));
        }

        [Theory]
        [InlineData("RSA-OAEP", "A256GCM")]      // weaker key wrap than we accept
        [InlineData("RSA-OAEP-256", "A128GCM")]  // different content encryption
        public void Decrypt_UnsupportedAlgorithm_ThrowsInvalidEncryptedPan(string alg, string enc)
        {
            // Only alg=RSA-OAEP-256 and enc=A256GCM are allowed (brief requirement 2).
            var encrypted = _jwe.Encrypt(Pan, alg: alg, enc: enc);

            AssertInvalid(() => _decryptor.Decrypt(encrypted));
        }

        [Fact]
        public void Decrypt_TamperedCiphertext_ThrowsInvalidEncryptedPan()
        {
            // Arrange: change one character of the ciphertext (part 4 of 5)
            var parts = _jwe.Encrypt(Pan).Split('.');
            parts[3] = (parts[3][0] == 'A' ? 'B' : 'A') + parts[3][1..];
            var tampered = string.Join('.', parts);

            // Assert: AES-GCM's tag check catches the change and we return a generic 400
            AssertInvalid(() => _decryptor.Decrypt(tampered));
        }

        [Theory]
        [InlineData("")]                       // empty
        [InlineData("not-a-jwe")]              // no dots
        [InlineData("a.b.c.d")]                // 4 parts instead of 5
        [InlineData("!!!.@@@.###.$$$.%%%")]    // 5 parts but not base64url
        public void Decrypt_MalformedInput_ThrowsInvalidEncryptedPan(string input)
        {
            AssertInvalid(() => _decryptor.Decrypt(input));
        }

        [Theory]
        [InlineData("41111111111")]            // 11 digits: too short
        [InlineData("41111111111111111111")]   // 20 digits: too long
        [InlineData("4111-1111-1111-1111")]    // not only digits
        public void Decrypt_PlaintextIsNotAPan_ThrowsInvalidEncryptedPan(string plaintext)
        {
            // Decryption works, but the content isn't a 12-19 digit PAN, so we reject it.
            var encrypted = _jwe.Encrypt(plaintext);

            AssertInvalid(() => _decryptor.Decrypt(encrypted));
        }

        [Fact]
        public void Decrypt_Failure_MessageNeverContainsThePan()
        {
            // The JWE decrypts fine, but the plaintext isn't a valid PAN (extra "X"), so it's rejected
            // AFTER the digits were recovered. The error message must still not echo them.
            var encrypted = _jwe.Encrypt(Pan + "X");

            var ex = Assert.Throws<CardControlException>(() => _decryptor.Decrypt(encrypted));

            Assert.DoesNotContain(Pan, ex.Message);
        }

        // Every failure looks the same to the caller: 400 InvalidEncryptedPan, no details.
        private static void AssertInvalid(Action act)
        {
            var ex = Assert.Throws<CardControlException>(act);
            Assert.Equal(400, ex.StatusCode);
            Assert.Equal(ErrorCodes.InvalidEncryptedPan, ex.Code);
        }
    }
}
