namespace CardControl.Model.Downstream
{
    public sealed record CardChangeRequest(string InitiatingUser, string? Channel);
}
