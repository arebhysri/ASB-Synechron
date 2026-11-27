using System.Net;

namespace CardControl.Model.Errors
{
    /// <summary>An expected failure, turned into Problem Details by the global exception handler.</summary>
    public sealed class CardControlException : Exception
    {
        public CardControlException(HttpStatusCode statusCode, string code, string message)
            : base(message)
        {
            StatusCode = (int)statusCode;
            Code = code;
        }

        public int StatusCode { get; }
        public string Code { get; }
    }
}
