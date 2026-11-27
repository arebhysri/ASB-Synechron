using System.Net;
using System.Text.Json;
using CardControl.IntegrationTest.Support;

namespace CardControl.IntegrationTest
{
    /// <summary>The OpenAPI document (used by Swagger UI) is served in Development and describes the API.</summary>
    public sealed class OpenApiTests : IDisposable
    {
        private readonly CardControlApiFactory _factory = new();

        public void Dispose() => _factory.Dispose();

        [Fact]
        public async Task OpenApiDocument_InDevelopment_ListsCardEndpointsAndBearerScheme()
        {
            // Arrange: no token needed, the document is public in Development
            using var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/openapi/v1.json");

            // Assert: served, has both endpoints and the Bearer (Authorize button) scheme
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            var paths = root.GetProperty("paths");
            Assert.True(paths.TryGetProperty("/api/Cards/lock", out _));
            Assert.True(paths.TryGetProperty("/api/Cards/unlock", out _));

            var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
            Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        }
    }
}
