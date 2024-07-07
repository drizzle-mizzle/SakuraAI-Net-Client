using SakuraAi;
using SakuraAi.Models.Common;

namespace SakuraAiClient.Test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public async Task Authorize()
        {
            using var client = new SakuraAi.SakuraAiClient();
            await client.InitializeAsync();

            var signInAttemptId = await client.SendLoginEmailAsync("drizzle-mizzle@hotmail.com");

            string token = string.Empty;
            for (int i = 0; i < 12; i++)
            {
                await Task.Delay(5000);
                var authorizedUser = await client.EnsureLoginByEmailAsync(signInAttemptId);
                if (authorizedUser.RefreshToken != "0") break;
            }

            Assert.That(token.Length > 1, Is.True);
        }

        [Test]
        public async Task Search()
        {
            using var client = new SakuraAi.SakuraAiClient();
            await client.InitializeAsync();

            var characters = await client.SearchAsync("Kurisu");

            Assert.That(characters.Any(character => character.name.Contains("Kurisu")), Is.True);
        }

        [Test]
        public async Task GetInfo()
        {
            using var client = new SakuraAi.SakuraAiClient();
            await client.InitializeAsync();

            var character = await client.GetCharacterInfoAsync("fqDaOBZ");

            Assert.That(character.name.Contains("Kurisu"), Is.True);
        }

        [Test]
        public async Task CreateNewChatAndSendMessage()
        {
            using var client = new SakuraAi.SakuraAiClient();
            await client.InitializeAsync();

            string SPECIFY_SOME_REFRESH_TOKEN_HERE = "";
            string SPECIFY_SOME_SESSION_ID_HERE = "sess_2gyJokeqozcxkmMpfJcBqAtFu3G";

            var user = new AuthorizedUser
            {
                RefreshToken = SPECIFY_SOME_REFRESH_TOKEN_HERE,
                SessionId = SPECIFY_SOME_SESSION_ID_HERE
            };

            var character = new SakuraCharacter
            {
                id = "fqDaOBZ",
                firstMessage = "Hey"
            };

            var chatId = await client.CreateNewChatAsync(user, character, "Hey");
            Assert.That(chatId, Has.Length.EqualTo(7));

            var responseMessage = await client.SendMessageToChatAsync(user, chatId, "Hey!");
            Assert.That(responseMessage.content.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(responseMessage.role, Is.EqualTo("assistant"));
        }

    }
}
