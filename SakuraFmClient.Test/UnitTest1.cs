
using System.Reflection;
using SakuraFm.Models;

namespace SakuraFm.Test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public async Task Initialize()
        {
            using var client = new SakuraFmClient();
            await client.InitializeAsync();

            Assert.That(client.GetType().GetField("Init", BindingFlags.NonPublic).GetValue(client) is true);
        }

        [Test]
        public async Task Authorize()
        {
            using var client = new SakuraFmClient();
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
            using var client = new SakuraFmClient();
            await client.InitializeAsync();

            var characters = await client.SearchAsync("Kurisu");

            Assert.That(characters.Any(character => character.name.Contains("Kurisu")), Is.True);
        }

        [Test]
        public async Task GetInfo()
        {
            using var client = new SakuraFmClient();
            await client.InitializeAsync();

            var character = await client.GetCharacterInfoAsync("fqDaOBZ");

            Assert.That(character.name.Contains("Kurisu"), Is.True);
        }

        [Test]
        public async Task CreateNewChatAndSendMessage()
        {
            using var client = new SakuraFmClient();
            await client.InitializeAsync();

            string SPECIFY_SOME_REFRESH_TOKEN_HERE =
                "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6ImNsaWVudF8yZ3lKbmlpOHZzVzVHRnN0YWR5UENUdkIwaXoiLCJyb3RhdGluZ190b2tlbiI6Im1iaHk1OWlrNWR3Zjg5ZXkxcDd1OWI4YWtzNWppOHh1dmw0cmp4amYifQ.DLz_QTK3g7cB985JSCHVCgS7TBcS83VDlw7hKr9jxATRQLVi5YeXpCMQQPc7nyh7Rs-ZM8-36NKKJ-C9kkhjPvBUUQ5pbSNQCPafD9sHwBT6osOa3zCPrrQc_6rQePODXR-uw-aZCTALsf0HXAYVAdW-8tekwjfMMtYR67WWFt-u5k_kP6wJKd9jJYC1p5FEKseZfOSiDhCZ-QW2hdhMC-jkPm4FWAJQx7xhhw78AOIBMVnaeBJyY-lM_08cubQmY3nyG-c0GD5Uj-P7VOe5_JVmcZX117ZK2B5zzowkZu6T51P1Wy3ClFg5dvn_K2aUvM0NAICuTsP40bi48IxDog";

            string SPECIFY_SOME_SESSION_ID_HERE =
                "sess_2gyJokeqozcxkmMpfJcBqAtFu3G";

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
