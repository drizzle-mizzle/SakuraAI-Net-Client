using SakuraAi.Client;
using SakuraAi.Client.Models.Common;

namespace SakuraAi.Test;


public class Tests
{
    [SetUp]
    public void Setup()
    {
    }


    [Test]
    public async Task Authorize()
    {
        using var client = new SakuraAiClient();

        var signInAttempt = await client.SendLoginEmailAsync("drizzle-mizzle@hotmail.com");

        for (var i = 0; i < 12; i++)
        {
            var authorizedUser = await client.EnsureLoginByEmailAsync(signInAttempt);
            if (authorizedUser is not null && authorizedUser.RefreshToken.Length > 1)
            {
                Assert.Pass();
                return;
            }

            await Task.Delay(3000);
        }

        Assert.Fail();
    }


    [Test]
    public async Task Search()
    {
        using var client = new SakuraAiClient();

        var characters = await client.SearchAsync("Arcueid");

        Assert.That(characters.Any(character => character.name.Contains("Arcueid")), Is.True);
    }


    [Test]
    public async Task GetInfo()
    {
        using var client = new SakuraAiClient();

        var character = await client.GetCharacterInfoAsync("fqDaOBZ");

        Assert.That(character.name, Does.Contain("Kurisu"));
    }


    [Test]
    public async Task CreateNewChatAndSendMessage()
    {
        using var client = new SakuraAiClient();

        const string SPECIFY_SOME_REFRESH_TOKEN_HERE = "";
        const string SPECIFY_SOME_SESSION_ID_HERE = "sess_2mIyAQYJt6mKblbZEEnfNvcZi0T";

        var user = new SakuraAuthorizedUser()
        {
            RefreshToken = SPECIFY_SOME_REFRESH_TOKEN_HERE,
            SessionId = SPECIFY_SOME_SESSION_ID_HERE
        };

        var character = new SakuraCharacter
        {
            id = "fqDaOBZ",
            firstMessage = "Hey"

        };

        var response = await client.CreateNewChatAsync(user.SessionId, user.RefreshToken, character, "Hey");
        Assert.Multiple(() =>
        {
            Assert.That(response.success, Is.True);
            Assert.That(response.chatId, Has.Length.EqualTo(7));
        });

        var responseMessage = await client.SendMessageToChatAsync(user.SessionId, user.RefreshToken, response.chatId, "Hey!");
        Assert.Multiple(() =>
        {
            Assert.That(responseMessage.content, Is.Not.Empty);
            Assert.That(responseMessage.role, Is.EqualTo("assistant"));
        });
    }
}
