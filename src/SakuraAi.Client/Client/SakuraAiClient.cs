using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using SakuraAi.Client.Exceptions;
using SakuraAi.Client.Models;
using SakuraAi.Client.Models.Common;
using SakuraAi.Client.Models.Http;
using SakuraAi.Client.Models.Http.Requests;
using SakuraAi.Client.Models.Http.Responses;

namespace SakuraAi.Client;


/// <inheritdoc />
public class SakuraAiClient : IDisposable
{
    private const int RefreshTimeout = 60_000; // minute
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    private RestClient _apiRestClient { get; } = new("https://api.sakura.fm");
    private RestClient _clerkRestClient { get; } = new("https://clerk.sakura.fm");
    private RestClient _sakuraRestClient { get; } = new("https://www.sakura.fm/en");
    private string _cookie { get; set; } = null!;


    public SakuraAiClient()
    {
        foreach (var p in _sakuraRestClient.DefaultParameters.ToArray())
        {
            _sakuraRestClient.DefaultParameters.RemoveParameter(p.Name!, p.Type);
        }

        foreach (var p in _clerkRestClient.DefaultParameters.ToArray())
        {
            _clerkRestClient.DefaultParameters.RemoveParameter(p.Name!, p.Type);
        }

        _sakuraRestClient.AddDefaultHeaders(new Dictionary<string, string>
        {
            { "Accept", "application/json" },
            { "Accept-Language", "en-US,en;q=0.5" },
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36" },
            { "Referer", "https://www.sakura.fm" },
            { "Rsc", "1" },
            { "Next-Url", "/en" }
        });

        _clerkRestClient.AddDefaultHeaders(new Dictionary<string, string>
        {
            { "Accept", "*/*" },
            { "Accept-Language", "en-US,en;q=0.5" },
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36" },
            { "Referer", "https://www.sakura.fm" },
            { "Rsc", "1" },
            { "Next-Url", "/en" }
        });

        Refresh();
    }


    /// <exception cref="SakuraAiException"></exception>
    /// <returns>loginAttemptId to be used in EnsureLoginByEmail()</returns>
    public async Task<SakuraSignInAttempt> SendLoginEmailAsync(string email)
    {
        Refresh();

        var request1 = new RestRequest("/v1/client/sign_ins", Method.Post);
        request1.AddHeader("Cookie", _cookie);
        request1.AddBody($"identifier={email}", ContentType.FormUrlEncoded);

        var response1 = await _clerkRestClient.ExecuteAsync(request1);
        if (response1.StatusCode is not HttpStatusCode.OK)
        {
            throw new SakuraAiException($"Failed to send login link to email {email}", (int)response1.StatusCode, HumanizeRestResponseError(response1));
        }

        var jContent = JsonConvert.DeserializeObject<JObject>(response1.Content!);
        var signInAttemptId = jContent?["response"]?["id"]?.ToString();
        if (signInAttemptId is null)
        {
            throw new SakuraAiException($"Failed to send login link to email {email}", (int)response1.StatusCode, HumanizeRestResponseError(response1));
        }

        var emailId = (jContent?["response"]?["supported_first_factors"] as JArray)?.First()?["email_address_id"]?.ToString();

        var request2 = new RestRequest($"/v1/client/sign_ins/{signInAttemptId}/prepare_first_factor", Method.Post);
        request2.AddBody($"email_address_id={emailId}&redirect_url=https://www.sakura.fm/ru/sign-in#/verify&strategy=email_link", ContentType.FormUrlEncoded);
        request2.AddHeader("Cookie", _cookie);

        var response2 = await _clerkRestClient.ExecuteAsync(request2);
        if (response2.StatusCode is not HttpStatusCode.OK)
        {
            throw new SakuraAiException($"Failed to prepare first factor {email}", (int)response2.StatusCode, HumanizeRestResponseError(response2));
        }

        return new SakuraSignInAttempt
        {
            Id = signInAttemptId,
            Cookie = _cookie,
            Email = email
        };
    }


    /// <exception cref="SakuraAiException"></exception>
    /// <returns>User's auth token</returns>
    public async Task<AuthorizedUser?> EnsureLoginByEmailAsync(SakuraSignInAttempt signInAttempt)
    {
        Refresh();

        var request = new RestRequest($"/v1/client/sign_ins/{signInAttempt.Id}");
        request.AddHeader("Cookie", signInAttempt.Cookie);
        var response = await _clerkRestClient.ExecuteAsync(request);

        if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.Unauthorized)
        {
            throw new SakuraAiException("Failed to authorize user", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var token = response.Cookies?.Cast<Cookie>().FirstOrDefault(c => c.Name.StartsWith("__client"));
        if (token?.Value is null or "0")
        {
            return null;
        }

        var jContent = JsonConvert.DeserializeObject<JObject>(response.Content!)!;
        var session = ((JArray)jContent["client"]!["sessions"]!).First!;
        var user = session["user"]!;

        var authorizedUser = new AuthorizedUser
        {
            UserId = (string)user["id"]!,
            Username = (string)user["username"]!,
            UserEmail = (string)jContent["response"]!["identifier"]!,
            UserImageUrl = (string)user["image_url"]!,
            RefreshToken = token.Value,
            ClientId = (string)jContent["client"]!["id"]!,
            SessionId = (string)session["id"]!
        };

        return authorizedUser;
    }


    /// <exception cref="SakuraAiException"></exception>
    /// <returns>Access token</returns>
    public async Task<string> GetAccessTokenAsync(string sessionId, string refreshToken)
    {
        Refresh();

        var request = new RestRequest($"/v1/client/sessions/{sessionId}/tokens?_clerk_js_version=5.5.0", Method.Post);
        request.AddHeader("Cookie", $"__client={refreshToken};{_cookie}");

        var response = await _clerkRestClient.ExecuteAsync(request);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new SakuraAiException("Failed to get access token", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var jContent = JsonConvert.DeserializeObject<JObject>(response.Content!)!;

        return (string)jContent["jwt"]!;
    }


    /// <exception cref="SakuraAiException"></exception>
    public async Task<string> CreateNewChatAsync(AuthorizedUser authorizedUser, SakuraCharacter character, string firstUserMessage, string locale = "en")
    {
        Refresh();

        var accessToken = await GetAccessTokenAsync(authorizedUser.SessionId, authorizedUser.RefreshToken);

        var request = new RestRequest("/api/chat", Method.Post);
        request.AddHeader("Cookie", _cookie);
        request.AddHeader("Authorization", $"Bearer {accessToken}");


        var createNewChatRequest = new RequestCreateNewChat
        {
            context = new InputNewChatContext
            {
                characterId = character.id,
                locale = locale,
                messages =
                [
                    new MessageObject
                    {
                        content = character.firstMessage,
                        role = "assistant",
                        type = "text"
                    }
                ]
            },
            action = new ActionObject
            {
                content = firstUserMessage,
                type = "append"
            }
        };

        request.AddJsonBody(JsonConvert.SerializeObject(createNewChatRequest));

        var response = await _apiRestClient.ExecuteAsync(request);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new SakuraAiException("Failed to create new chat", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var content = response.Content?.Split('\n').Last(line => line.Contains("success"));
        if (content is null)
        {
            throw new SakuraAiException("Failed to create new chat", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        content = Regex.Unescape(content[start..++end]);

        var chatResponse = JsonConvert.DeserializeObject<ChatResponse>(content)!;
        if (chatResponse.success is false)
        {
            throw new SakuraAiException("Failed to create new chat", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        return chatResponse.chatId;
    }


    /// <returns>Character response message</returns>
    /// <exception cref="SakuraAiException"></exception>
    public async Task<MessageObject> SendMessageToChatAsync(AuthorizedUser authorizedUser, string chatId, string message, string locale = "en")
    {
        Refresh();

        var accessToken = await GetAccessTokenAsync(authorizedUser.SessionId, authorizedUser.RefreshToken);

        var request = new RestRequest("/api/chat", Method.Post);
        request.AddHeader("Cookie", _cookie);
        request.AddHeader("Authorization", $"Bearer {accessToken}");

        var createNewChatRequest = new RequestSendMessage
        {
            context = new InputMessageContext
            {
                chatId = chatId,
                locale = locale
            },
            action = new ActionObject
            {
                content = message,
                type = "append"
            }
        };

        request.AddJsonBody(JsonConvert.SerializeObject(createNewChatRequest));

        var response = await _apiRestClient.ExecuteAsync(request);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new SakuraAiException("Failed to send message", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var content = response.Content?.Split('\n').Last(line => line.Contains("success"));
        if (content is null)
        {
            throw new SakuraAiException("Failed to send message", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        content = Regex.Unescape(content[start..++end]);

        var chatResponse = JsonConvert.DeserializeObject<ChatResponse>(content)!;
        if (chatResponse.success is false)
        {
            throw new SakuraAiException("Failed to send message", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        return chatResponse.messages.Last();
    }


    public enum CategoryMatchType { any, all }


    /// <exception cref="SakuraAiException"></exception>
    public async Task<ICollection<SakuraCharacter>> SearchAsync(string query, bool allowNsfw = true, Category[]? categories = null, CategoryMatchType categoryMatchType = CategoryMatchType.any)
    {
        var request = new RestRequest("/");
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

        var response = await _sakuraRestClient.ExecuteAsync(request);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new SakuraAiException("Failed to perform search", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var neededLine = response.Content!.Split('\n').Last(line => line.Contains("\"characters\""));
        neededLine = neededLine[neededLine.IndexOf("{\"characters\"", StringComparison.Ordinal)..];

        var jContent = JsonConvert.DeserializeObject<JObject>(neededLine)!;
        var characters = JsonConvert.DeserializeObject<List<SakuraCharacter>>(jContent["characters"]!.ToString())!;

        return characters;
    }


    /// <exception cref="SakuraAiException"></exception>
    public async Task<SakuraCharacter> GetCharacterInfoAsync(string characterId)
    {
        var request = new RestRequest($"/chat/{characterId}");

        var response = await _sakuraRestClient.ExecuteAsync(request);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new SakuraAiException("Failed to get character info", (int)response.StatusCode, HumanizeRestResponseError(response));
        }

        var neededLine = response.Content!.Split('\n').First(line => line.Contains("\"success\""));
        neededLine = neededLine[neededLine.IndexOf('{')..];

        var jContent = JsonConvert.DeserializeObject<JObject>(neededLine)!;
        var character = JsonConvert.DeserializeObject<SakuraCharacter>(jContent["data"]!["character"]!.ToString())!;

        return character;
    }


    // Private


    private static string HumanizeRestResponseError(RestResponse? response)
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


    private static string CategoryToString(Category cat)
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


    private void Refresh()
    {
        if (_cookie is not null && _sw.ElapsedMilliseconds < RefreshTimeout)
        {
            return;
        }

        lock (_apiRestClient)
        {
            lock (_sakuraRestClient)
            {
                lock (_clerkRestClient)
                {
                    var request = new RestRequest("/v1/client");
                    var response = _clerkRestClient.Execute(request, Method.Get);

                    var cookies = response.Cookies!.Cast<Cookie>().ToArray();
                    _cookie = string.Join(';', cookies.Select(c => $"{c.Name}={c.Value}"));
                }
            }
        }

        _sw.Restart();
    }


    #region Dispose

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposedValue)
        {
            return;
        }

        _clerkRestClient.Dispose();
        _sakuraRestClient.Dispose();
        _apiRestClient.Dispose();

        _disposedValue = true;
    }


    private bool _disposedValue;

    #endregion Dispose
}
