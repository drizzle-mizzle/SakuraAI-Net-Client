using System.Linq;
using System.Linq.Expressions;
using System.Diagnostics;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using SakuraFm.Exceptions;
using SakuraFm.Models;
using SakuraFm.Models.Http;

namespace SakuraFm
{
    public class SakuraFmClient : IDisposable
    {
        private const int refreshTimeout = 60_000; // minute
        internal RestClient ApiRestClient { get; set; } = null!;
        internal RestClient SakuraRestClient { get; set; } = null!;
        internal RestClient ClerkRestClient { get; set; } = null!;
        internal string Cookie { get; set; } = null!;
        internal bool Init { get; set; } = false;

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public void Refresh()
        {
            if (_sw.ElapsedMilliseconds < refreshTimeout)
            {
                return;
            }

            lock (ClerkRestClient)
            {
                foreach (var param in ClerkRestClient.DefaultParameters.ToArray())
                {
                    ClerkRestClient.DefaultParameters.RemoveParameter(param.Name!, param.Type);
                }

                this.InitializeAsync().Wait();
            }

            _sw.Restart();
        }


        #region Dispose

        private bool _disposedValue = false;

        private void Dispose(bool disposing)
        {
            if (_disposedValue)
            {
                return;
            }

            ClerkRestClient.Dispose();
            SakuraRestClient.Dispose();
            ApiRestClient.Dispose();

            _disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion Dispose
    }

    public static class SakuraFmClientExt
    {
        public static async Task InitializeAsync(this SakuraFmClient sakuraFmClient)
        {
            sakuraFmClient.ApiRestClient = new RestClient("https://api.sakura.fm");
            sakuraFmClient.SakuraRestClient = new RestClient("https://www.sakura.fm");
            sakuraFmClient.SakuraRestClient.AddDefaultHeaders(new Dictionary<string, string>
            {
                { "Accept", "application/json" },
                { "Accept-Encoding", "gzip, deflate, br, zstd" },
                { "Accept-Language", "en-US,en;q=0.5" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36" },
                { "Referer", "https://www.sakura.fm" },
                { "Rsc", "1" },
                { "Next-Url", "/en"}
            });

            sakuraFmClient.ClerkRestClient = new RestClient("https://clerk.sakura.fm");
            var request = new RestRequest("/", Method.Get);
            var response = await sakuraFmClient.ClerkRestClient.ExecuteAsync(request);

            sakuraFmClient.Cookie = string.Join(';', response.Cookies!.Cast<Cookie>().Select(c => $"{c.Name}={c.Value}"));
            sakuraFmClient.ClerkRestClient.AddDefaultHeaders(new Dictionary<string, string>
            {
                { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" },
                { "Accept-Encoding", "gzip, deflate, br, zstd" },
                { "Accept-Language", "en-US,en;q=0.5" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36" },
                { "Referer", "https://www.sakura.fm" }
            });

            sakuraFmClient.Init = true;
        }

        /// <param name="sakuraFmClient"></param>
        /// <param name="email"></param>
        /// <returns>loginAttemptId to be used in EnsureLoginByEmail()</returns>
        /// <exception cref="OperationFailedException"></exception>
        public static async Task<string> SendLoginEmailAsync(this SakuraFmClient sakuraFmClient, string email)
        {
            sakuraFmClient.Refresh();

            var request1 = new RestRequest("/v1/client/sign_ins?_clerk_js_version=5.2.3", Method.Post);
            request1.AddHeader("Cookie", sakuraFmClient.Cookie);
            request1.AddBody($"identifier={email}", ContentType.FormUrlEncoded);

            var response1 = await sakuraFmClient.ClerkRestClient.ExecuteAsync(request1);
            if (response1.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to send login link to email {email} | Details: {response1.HumanizeRestResponseError()}");
            }

            var jContent = JsonConvert.DeserializeObject<JObject>(response1.Content!);
            var signInAttemptId = jContent?["response"]?["id"]?.ToString();
            if (signInAttemptId is null)
            {
                throw new OperationFailedException($"Failed to send login link to email {email} | Details: {response1.HumanizeRestResponseError()}");
            }

            var emailId = (jContent?["response"]?["supported_first_factors"] as JArray)?.First()?["email_address_id"]?.ToString();
            var clientIdCookie = response1.Cookies?.Cast<Cookie>().FirstOrDefault(c => c.Name.Equals("__client"));

            var request2 = new RestRequest($"/v1/client/sign_ins/{signInAttemptId}/prepare_first_factor?_clerk_js_version=5.2.4 ", Method.Post);
            request2.AddBody($"email_address_id={emailId}&redirect_url=https://www.sakura.fm/ru/sign-in#/verify&strategy=email_link", ContentType.FormUrlEncoded);

            var cookie = (string)sakuraFmClient.ClerkRestClient.DefaultParameters.First(p => p.Name == "Cookie").Value!;
            sakuraFmClient.ClerkRestClient.DefaultParameters.ReplaceParameter(Parameter.CreateParameter("Cookie", $"{cookie};{clientIdCookie?.Name}={clientIdCookie?.Value};", ParameterType.HttpHeader));
            var response2 = await sakuraFmClient.ClerkRestClient.ExecuteAsync(request2);
            if (response2.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to prepare first factor {email} | Details: {response2.HumanizeRestResponseError()}");
            }

            return signInAttemptId;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sakuraFmClient"></param>
        /// <param name="signInAttemptId"></param>
        /// <returns>User's auth token</returns>
        public static async Task<AuthorizedUser> EnsureLoginByEmailAsync(this SakuraFmClient sakuraFmClient, string signInAttemptId)
        {
            sakuraFmClient.Refresh();

            var request = new RestRequest($"/v1/client/sign_ins/{signInAttemptId}?_clerk_js_version=5.2.4", Method.Get);
            request.AddHeader("Cookie", sakuraFmClient.Cookie);
            var response = await sakuraFmClient.ClerkRestClient.ExecuteAsync(request);
            if (response.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to authorize user | Details: {response.HumanizeRestResponseError()}");
            }

            var token = response.Cookies?.Cast<Cookie>().FirstOrDefault(c => c.Name.StartsWith("__client"));
            if (token is null)
            {
                throw new OperationFailedException($"Failed to authorize user | Details: {response.HumanizeRestResponseError()}");
            }

            var jContent = JsonConvert.DeserializeObject<JObject>(response.Content!)!;
            var session = ((JArray)jContent["client"]!["sessions"]!).First!;
            var user = session["user"]!;

            var result = new AuthorizedUser
            {
                UserId = (string)user["id"]!,
                Username = (string)user["username"]!,
                UserEmail = (string)jContent["response"]!["identifier"]!,
                UserImageUrl = (string)user["image_url"]!,
                RefreshToken = token.Value,
                ClientId = (string)jContent["client"]!["id"]!,
                SessionId = (string)session["id"]!
            };

            return result;
        }

        /// <param name="sakuraFmClient"></param>
        /// <param name="sessionId"></param>
        /// <param name="refreshToken"></param>
        /// <returns>Access token</returns>
        public static async Task<string> GetAccessTokenAsync(this SakuraFmClient sakuraFmClient, string sessionId, string refreshToken)
        {
            sakuraFmClient.Refresh();

            var request = new RestRequest($"/v1/client/sessions/{sessionId}/tokens?_clerk_js_version=5.5.0", Method.Post);
            request.AddHeader("Cookie", $"__client={refreshToken};{sakuraFmClient.Cookie}");

            var response = await sakuraFmClient.ClerkRestClient.ExecuteAsync(request);
            if (response.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to get access token | Details: {response.HumanizeRestResponseError()}");
            }

            var jContent = JsonConvert.DeserializeObject<JObject>(response.Content!)!;

            return (string)jContent["jwt"]!;
        }


        /// <summary>
        ///
        /// </summary>
        /// <param name="sakuraFmClient"></param>
        /// <param name="authorizedUser"></param>
        /// <param name="character"></param>
        /// <param name="firstUserMessage"></param>
        /// <param name="locale"></param>
        /// <returns></returns>
        public static async Task<string> CreateNewChatAsync(this SakuraFmClient sakuraFmClient, AuthorizedUser authorizedUser, SakuraCharacter character, string firstUserMessage, string locale = "en")
        {
            sakuraFmClient.Refresh();

            var accessToken = await sakuraFmClient.GetAccessTokenAsync(authorizedUser.SessionId, authorizedUser.RefreshToken);

            var request = new RestRequest("/api/chat", Method.Post);
            request.AddHeader("Authorization", $"Bearer {accessToken}");

            var createNewChatRequest = new RequestCreateNewChat
            {
                context = new InputNewChatContext { characterId = character.id, locale = locale, messages = [ new MessageObject { content = character.firstMessage, role = "assistant", type = "text" } ] },
                action = new ActionObject { content = firstUserMessage, type = "append" }
            };

            request.AddJsonBody(JsonConvert.SerializeObject(createNewChatRequest));

            var response = await sakuraFmClient.ApiRestClient.ExecuteAsync(request);
            if (response.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to create new chat | Details: {response.HumanizeRestResponseError()}");
            }

            var result = JsonConvert.DeserializeObject<ChatResponse>(response.Content!)!;
            if (result.success is false)
            {
                throw new OperationFailedException($"Failed to create new chat | Details: {response.HumanizeRestResponseError()}");
            }

            return result.chatId;
        }

        /// <param name="sakuraFmClient"></param>
        /// <param name="authorizedUser"></param>
        /// <param name="chatId"></param>
        /// <param name="message"></param>
        /// <param name="locale"></param>
        /// <returns>Character response message</returns>
        /// <exception cref="OperationFailedException"></exception>
        public static async Task<MessageObject> SendMessageToChatAsync(this SakuraFmClient sakuraFmClient, AuthorizedUser authorizedUser, string chatId, string message, string locale = "en")
        {
            sakuraFmClient.Refresh();

            var accessToken = await sakuraFmClient.GetAccessTokenAsync(authorizedUser.SessionId, authorizedUser.RefreshToken);

            var request = new RestRequest("/api/chat", Method.Post);
            request.AddHeader("Authorization", $"Bearer {accessToken}");

            var createNewChatRequest = new RequestSendMessage
            {
                context = new InputMessageContext { chatId = chatId, locale = locale },
                action = new ActionObject { content = message, type = "append" }
            };

            request.AddJsonBody(JsonConvert.SerializeObject(createNewChatRequest));
            var response = await sakuraFmClient.ApiRestClient.ExecuteAsync(request);
            if (response.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to send message | Details: {response.HumanizeRestResponseError()}");
            }

            var result = JsonConvert.DeserializeObject<ChatResponse>(response.Content!)!;
            if (result.success is false)
            {
                throw new OperationFailedException($"Failed to send message | Details: {response.HumanizeRestResponseError()}");
            }

            return result.messages.Last();
        }


        public enum CategoryMatchType { any, all }
        /// <summary>
        ///
        /// </summary>
        /// <param name="sakuraFmClient"></param>
        /// <param name="query"></param>
        /// <param name="allowNsfw"></param>
        /// <param name="categories"></param>
        /// <param name="categoryMatchType"></param>
        /// <returns></returns>
        /// <exception cref="OperationFailedException"></exception>
        public static async Task<IReadOnlyList<SakuraCharacter>> SearchAsync(this SakuraFmClient sakuraFmClient, string query,
                                                                             bool allowNsfw = true, Category[]? categories = null,
                                                                             CategoryMatchType categoryMatchType = CategoryMatchType.any)
        {
            var request = new RestRequest("/", Method.Get);
            request.AddQueryParameter("search", query);
            request.AddQueryParameter("allowNsfw", allowNsfw.ToString().ToLower());

            if (categories is not null)
            {
                var stringCategories = string.Join(',', categories.Select(CategoryToString));
                request.AddQueryParameter("categories", stringCategories);

                if (categories.Length > 1)
                {
                    request.AddQueryParameter("matchType", categoryMatchType.ToString("G"));
                }
            }

            var response = await sakuraFmClient.SakuraRestClient.ExecuteAsync(request);
            if (response.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to perform search | Details: {response.HumanizeRestResponseError()}");
            }

            string neededLine = response.Content!.Split('\n').First(line => line.Contains("\"characters\""));
            neededLine = neededLine[neededLine.IndexOf('{')..];

            var jContent = JsonConvert.DeserializeObject<JObject>(neededLine)!;
            var characters = JsonConvert.DeserializeObject<List<SakuraCharacter>>(jContent["characters"]!.ToString())!;

            return characters;
        }

        public static async Task<SakuraCharacter> GetCharacterInfoAsync(this SakuraFmClient sakuraFmClient, string characterId)
        {
            var request = new RestRequest($"/chat/{characterId}", Method.Get);

            var response = await sakuraFmClient.SakuraRestClient.ExecuteAsync(request);
            if (response.StatusCode is not HttpStatusCode.OK)
            {
                throw new OperationFailedException($"Failed to get character info | Details: {response.HumanizeRestResponseError()}");
            }

            string neededLine = response.Content!.Split('\n').First(line => line.Contains("\"success\""));
            neededLine = neededLine[neededLine.IndexOf('{')..];

            var jContent = JsonConvert.DeserializeObject<JObject>(neededLine)!;
            var character = JsonConvert.DeserializeObject<SakuraCharacter>(jContent["data"]!["character"]!.ToString())!;

            return character;
        }


        // Private

        private static string HumanizeRestResponseError(this RestResponse? response)
        {
            string details;
            if (response is null)
            {
                details = "Failed to get response from SakuraFM";
            }
            else
            {
                details = $"{response.StatusCode:D} ({response.StatusDescription})";
                details += $"\nHeaders: {(response.Headers is null || response.Headers.Count == 0 ? "none" : "\n" + string.Join("\n", response.Headers.Select(h => $"[ '{h.Name}'='{h.Value}' ]")))}";
                details += $"\nContent: {(string.IsNullOrEmpty(response.Content) ? "none" : response.Content)}";
            }

            return details;
        }

        private static string CategoryToString(this Category cat)
        {
            return cat switch
            {
                Category.Male => "Male",
                Category.Female => "Female",
                Category.Anime => "Anime",
                Category.MoviesAndTV => "Movies & TV",
                Category.Yandere => "Yandere",
                Category.Tsundere => "Tsundere",
                Category.Gay => "Gay",
                Category.Lesbian => "Lesbian",
                Category.Femboy => "Femboy",
                Category.Futanari => "Futanari",
                Category.VideoGames => "Video Games",
                Category.Furry => "Furry",
                Category.Horror => "Horror",
                Category.OC => "OC",
                Category.Vampire => "Vampire",
                Category.NonBinary => "Non-binary",
                Category.Dominant => "Dominant",
                Category.Submissive => "Submissive",
                Category.MILF => "MILF",
                Category.DILF => "FILD",
                _ => throw new ArgumentOutOfRangeException(nameof(cat), cat, null)
            };
        }

    }
}
