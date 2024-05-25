namespace SakuraFmClient.Models;

public class AuthorizedUser
{
    public AuthorizedUser() { }

    public AuthorizedUser(string username, string userEmail, string refreshToken, string sessionId, string clientId)
    {
        Username = username;
        UserEmail = userEmail;
        RefreshToken = refreshToken;
        SessionId = sessionId;
        ClientId = clientId;
    }

    public string UserId { get; set; }
    public string Username { get; set; }
    public string UserEmail { get; set; }
    public string UserImageUrl { get; set; }
    public string RefreshToken { get; set; }
    public string ClientId { get; set; }
    public string SessionId { get; set; }
}
