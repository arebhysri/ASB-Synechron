using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace CardControl.Model.Requests
{
    public sealed class CardActionRequest
    {
        [Required]
        public string EncryptedPan { get; init; } = string.Empty;
        [Required]
        public string CorporateId { get; init; } = string.Empty;
    }
}
