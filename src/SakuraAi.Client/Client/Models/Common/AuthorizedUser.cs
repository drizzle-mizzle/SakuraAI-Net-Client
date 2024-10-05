namespace SakuraAi.Client.Models.Common;

public class AuthorizedUser
{
    public string UserId { get; set; }
    public string Username { get; set; }
    public string UserEmail { get; set; }
    public string UserImageUrl { get; set; }
    public string RefreshToken { get; set; }
    public string ClientId { get; set; }
    public string SessionId { get; set; }
}
