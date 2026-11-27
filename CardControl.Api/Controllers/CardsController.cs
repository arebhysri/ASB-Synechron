using CardControl.Data.Options;
using CardControl.Interface;
using CardControl.Model.Enums;
using CardControl.Model.Errors;
using CardControl.Model.Requests;
using CardControl.Model.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace CardControl.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = AuthOptions.PolicyName)]
    public sealed class CardsController(
        ICardControlService cardControlService,
        ILogger<CardsController> logger) : ControllerBase
    {
        private const int MaxInitiatingUserLength = 128;

        /// <summary>Lock a corporate card.</summary>
        /// <param name="initiatingUser">ID of the person in the partner platform who started the action (max 128 characters). Recorded in the audit trail.</param>
        /// <param name="request">Corporate ID and the PAN as a compact JWE.</param>
        /// <param name="cancellationToken">Request cancellation.</param>
        /// <response code="200">Card locked. The PAN is returned masked.</response>
        /// <response code="400">Missing/invalid X-Initiating-User header, invalid JWE or invalid body.</response>
        /// <response code="401">Missing, expired or invalid access token.</response>
        /// <response code="403">Token has no CardControl.Write role, or the corporate is not enabled.</response>
        /// <response code="404">Card not found for this corporate.</response>
        /// <response code="409">Card is already locked.</response>
        /// <response code="422">Card is cancelled.</response>
        /// <response code="502">Downstream service returned an unexpected error.</response>
        /// <response code="503">Downstream service is unavailable.</response>
        [HttpPost("lock")]
        [ProducesResponseType<CardActionResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
        public Task<CardActionResponse> Lock(
            [FromHeader(Name = "X-Initiating-User")] string? initiatingUser,
            [FromBody] CardActionRequest request,
            CancellationToken cancellationToken)
            => ExecuteAsync(CardAction.Lock, initiatingUser, request, cancellationToken);

        /// <summary>Unlock a corporate card.</summary>
        /// <param name="initiatingUser">ID of the person in the partner platform who started the action (max 128 characters). Recorded in the audit trail.</param>
        /// <param name="request">Corporate ID and the PAN as a compact JWE.</param>
        /// <param name="cancellationToken">Request cancellation.</param>
        /// <response code="200">Card unlocked. The PAN is returned masked.</response>
        /// <response code="400">Missing/invalid X-Initiating-User header, invalid JWE or invalid body.</response>
        /// <response code="401">Missing, expired or invalid access token.</response>
        /// <response code="403">Token has no CardControl.Write role, or the corporate is not enabled.</response>
        /// <response code="404">Card not found for this corporate.</response>
        /// <response code="409">Card is not locked.</response>
        /// <response code="422">Card is cancelled.</response>
        /// <response code="502">Downstream service returned an unexpected error.</response>
        /// <response code="503">Downstream service is unavailable.</response>
        [HttpPost("unlock")]
        [ProducesResponseType<CardActionResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
        public Task<CardActionResponse> Unlock(
            [FromHeader(Name = "X-Initiating-User")] string? initiatingUser,
            [FromBody] CardActionRequest request,
            CancellationToken cancellationToken)
            => ExecuteAsync(CardAction.Unlock, initiatingUser, request, cancellationToken);

        private async Task<CardActionResponse> ExecuteAsync(
            CardAction action,
            string? initiatingUser,
            CardActionRequest request,
            CancellationToken cancellationToken)
        {
            var user = initiatingUser?.Trim();
            if (string.IsNullOrEmpty(user) || user.Length > MaxInitiatingUserLength)
                throw new CardControlException(HttpStatusCode.BadRequest, ErrorCodes.InvalidInitiatingUser,
                    $"The X-Initiating-User header is required (max {MaxInitiatingUserLength} characters).");

            var response = await cardControlService.ExecuteAsync(action, request, user, cancellationToken);

            // No PAN, JWE or card token in logs: only the action, corporate and resulting status.
            logger.LogInformation("Card {Action} succeeded for corporate {CorporateId}, status {Status}",
                action, request.CorporateId, response.Status);

            return response;
        }
    }
}
