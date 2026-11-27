using System.Net;
using CardControl.Model.Errors;

namespace CardControl.Repository.Downstream
{
    /// <summary>
    /// Sends a downstream HTTP request and turns transport failures (service down, connection reset,
    /// HttpClient timeout) into a 503, instead of letting them surface as a 500.
    /// </summary>
    internal static class DownstreamTransport
    {
        public static async Task<HttpResponseMessage> SendAsync(
            Func<CancellationToken, Task<HttpResponseMessage>> send,
            string serviceName,
            CancellationToken cancellationToken)
        {
            try
            {
                return await send(cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw Unavailable(serviceName);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout elapsed. If the caller cancelled, the exception is left to propagate.
                throw Unavailable(serviceName);
            }
        }

        private static CardControlException Unavailable(string serviceName) =>
            new(HttpStatusCode.ServiceUnavailable, ErrorCodes.DownstreamUnavailable,
                $"The {serviceName} is temporarily unavailable.");
    }
}
