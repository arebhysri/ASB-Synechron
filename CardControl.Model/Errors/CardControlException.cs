using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Model.Errors
{
    public sealed class CardControlException : Exception
    {
        public CardControlException(int stausCode, string code, string message) : base(message)
        {
            StatusCode = stausCode;
            Code = code;
        }

        public int StatusCode { get; }
        public string Code { get; }
    }
}
