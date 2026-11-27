namespace CardControl.Interface
{
    public interface ITokenisationClient
    {
        Task<string> GetCardTokenAsync(string pan, CancellationToken cancellationToken);
    }
}
