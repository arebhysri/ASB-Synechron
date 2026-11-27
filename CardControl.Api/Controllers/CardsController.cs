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

        [HttpPost("lock")]
        public Task<CardActionResponse> Lock(
            [FromHeader(Name = "X-Initiating-User")] string? initiatingUser,
            [FromBody] CardActionRequest request,
            CancellationToken cancellationToken)
            => ExecuteAsync(CardAction.Lock, initiatingUser, request, cancellationToken);

        [HttpPost("unlock")]
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
