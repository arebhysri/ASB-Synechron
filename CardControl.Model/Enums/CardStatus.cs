using System.Text.Json.Serialization;

namespace CardControl.Model.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter<CardStatus>))]
    public enum CardStatus
    {
        Active,
        Locked,
        Cancelled
    }
}
