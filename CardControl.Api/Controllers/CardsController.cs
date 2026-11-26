using CardControl.Interface;
using CardControl.Model.Enums;
using CardControl.Model.Errors;
using CardControl.Model.Requests;
using CardControl.Model.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CardControl.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "CardControlWrite")]
    public sealed class CardsController(ICardControlService cardControlService) : ControllerBase
    {
        private const int MaxInitiatingUserLength = 128;

        [HttpPost("lock")]
        public Task<CardActionResponse> Lock([FromHeader(Name = "X-Initiating-User")] string? initiatingUser, [FromBody] CardActionRequest request, CancellationToken cancellationToken)
            => ExecuteAsync(CardAction.Lock, initiatingUser, request, cancellationToken);

        [HttpPost("unlock")]
        public Task<CardActionResponse> Unlock([FromHeader(Name = "X-Initiating-User")] string? initiatingUser, [FromBody] CardActionRequest request, CancellationToken cancellationToken)
            => ExecuteAsync(CardAction.Unlock, initiatingUser, request, cancellationToken);

        private Task<CardActionResponse> ExecuteAsync(CardAction action, string? initiatingUser, CardActionRequest request, CancellationToken cancellationToken)
        {
            var user = initiatingUser?.Trim();
            if (string.IsNullOrEmpty(user) || user.Length > MaxInitiatingUserLength)
                throw new CardControlException(400, "InvalidInitiatingUser",
                    $"The X-Initiating-User header is required (max {MaxInitiatingUserLength} characters).");
            
            return cardControlService.ExecuteAsync(action, request, user, cancellationToken);
        }
    }
}
