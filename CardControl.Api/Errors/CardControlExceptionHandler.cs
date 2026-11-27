using CardControl.Model.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CardControl.Api.Errors
{
    /// <summary>The single global exception handler: every error becomes RFC 9457 Problem Details with a "code".</summary>
    public sealed class CardControlExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<CardControlExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            int status;
            string code;
            string detail;

            if (exception is CardControlException ex)
            {
                status = ex.StatusCode;
                code = ex.Code;
                detail = ex.Message;
                logger.LogWarning("Card control request failed with {Code} ({Status})", code, status);
            }
            else
            {
                status = StatusCodes.Status500InternalServerError;
                code = ErrorCodes.InternalServerError;
                detail = "An unexpected error occurred.";
                // Only the exception type is logged: its message could contain sensitive input.
                logger.LogError("Unhandled {ExceptionType}", exception.GetType().Name);
            }

            context.Response.StatusCode = status;
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = status,
                    Title = code,
                    Detail = detail,
                    Extensions = { ["code"] = code }
                }
            });
        }
    }
}
