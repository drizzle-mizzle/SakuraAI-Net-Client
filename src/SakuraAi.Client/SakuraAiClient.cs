using System.Diagnostics;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

    private const string CLERK_URI = "https://clerk.sakura.fm";
    private const string API_URI_BASE = "https://api.sakura.fm/api";
    private const string FRONTEND_URI_BASE = "https://www.sakura.fm";

    private readonly HttpClient HTTP_CLIENT;
    private readonly Stopwatch _sw;

    private string TEMP_COOKIES_SET { get; set; } = null!;


    public SakuraAiClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        HTTP_CLIENT = new HttpClient(handler);

        (string header, string value)[] defaultHeaders =
        [
            ("Accept", "application/json"),
            ("Accept-Encoding", "gzip, deflate"),
            ("Accept-Language", "en-US,en;q=0.5"),
            ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"),
            ("Referer", "https://www.sakura.fm"),
            ("RSC", "1")
        ];

        foreach (var dh in defaultHeaders)
        {
            HTTP_CLIENT.DefaultRequestHeaders.Add(dh.header, dh.value);
        }

        _sw = Stopwatch.StartNew();

        Refresh();
    }


    /// <exception cref="SakuraException"></exception>
    /// <returns>loginAttemptId to be used in EnsureLoginByEmail()</returns>
    public async Task<SakuraSignInAttempt> SendLoginEmailAsync(string email)
    {
        Refresh(force: true);

        var request1 = new HttpRequestMessage(HttpMethod.Post, $"{CLERK_URI}/v1/client/sign_ins");
        request1.Headers.Add("Cookie", TEMP_COOKIES_SET);
        request1.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("identifier", email)
        });

        var response1 = await HTTP_CLIENT.SendAsync(request1);
        var content1 = await response1.Content.ReadAsStringAsync();

        if (!response1.IsSuccessStatusCode)
        {
            throw new SakuraException($"Failed to send login link to email {email}: {GetErrorDescription(content1)}", (int)response1.StatusCode, HumanizeHttpResponseError(response1));
        }

        var jContent1 = JsonConvert.DeserializeObject<JObject>(content1);
        var signInAttemptId = jContent1?["response"]?["id"]?.ToString();
        var emailId = (jContent1?["response"]?["supported_first_factors"] as JArray)?.FirstOrDefault(sff => sff["email_address_id"] is not null)?["email_address_id"]?.ToString();

        if (signInAttemptId is null || emailId is null)
        {
            throw new SakuraException($"Failed to send login link to email {email}: signInAttemptId or emailId is missing", (int)response1.StatusCode, HumanizeHttpResponseError(response1));
        }

        var request2 = new HttpRequestMessage(HttpMethod.Post, $"{CLERK_URI}/v1/client/sign_ins/{signInAttemptId}/prepare_first_factor");
        request2.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("email_address_id", emailId),
            new("redirect_url", $"{FRONTEND_URI_BASE}/sign-in#/verify"),
            new("strategy", "email_link")
        });

        request2.Headers.Add("Cookie", TEMP_COOKIES_SET);

        var response2 = await HTTP_CLIENT.SendAsync(request2);
        if (!response2.IsSuccessStatusCode)
        {
            var content2 = await response2.Content.ReadAsStringAsync();
            throw new SakuraException($"Failed to prepare first factor {email}: {GetErrorDescription(content2)}", (int)response2.StatusCode, HumanizeHttpResponseError(response2));
        }

        return new SakuraSignInAttempt
        {
            Id = signInAttemptId,
            Cookie = TEMP_COOKIES_SET,
            Email = email
        };
    }


    private static readonly Regex CLIENT_COOKIE_REGEX = new(@"(?<=__client(_uat)?\=)[\S]*(?=;)");
    /// <exception cref="SakuraException"></exception>
    /// <returns>User's auth token</returns>
    public async Task<SakuraAuthorizedUser?> EnsureLoginByEmailAsync(SakuraSignInAttempt signInAttempt)
    {
        Refresh();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{CLERK_URI}/v1/client/sign_ins/{signInAttempt.Id}");
        request.Headers.Add("Cookie", signInAttempt.Cookie);

        var response = await HTTP_CLIENT.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.Unauthorized)
        {
            throw new SakuraException($"Failed to authorize user: {GetErrorDescription(content)}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var cookies = response.Headers.Single(h => h.Key.ToLower().StartsWith("set-cookie")).Value;
        var clientCookie = cookies.FirstOrDefault(h => h.StartsWith("__client"));
        var token = clientCookie is null ? null : CLIENT_COOKIE_REGEX.Match(clientCookie).Value;

        if (token is null or "0")
        {
            return null;
        }

        var jContent = JsonConvert.DeserializeObject<JObject>(content)!;
        var session = ((JArray)jContent["client"]!["sessions"]!).First!;
        var user = session["user"]!;

        var authorizedUser = new SakuraAuthorizedUser
        {
            UserId = (string)user["id"]!,
            Username = (string)user["username"]!,
            UserEmail = (string)jContent["response"]!["identifier"]!,
            UserImageUrl = (string)user["image_url"]!,
            RefreshToken = token,
            ClientId = (string)jContent["client"]!["id"]!,
            SessionId = (string)session["id"]!
        };

        return authorizedUser;
    }


    /// <exception cref="SakuraException"></exception>
    /// <returns>Access token</returns>
    public async Task<string> GetAccessTokenAsync(string sessionId, string refreshToken)
    {
        Refresh();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{CLERK_URI}/v1/client/sessions/{sessionId}/tokens?_clerk_js_version=5.5.0");
        request.Headers.Add("Cookie", $"__client={refreshToken};{TEMP_COOKIES_SET}");

        var response = await HTTP_CLIENT.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new SakuraException($"Failed to get access token: {GetErrorDescription(content)}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var jContent = JsonConvert.DeserializeObject<JObject>(content)!;

        return (string)jContent["jwt"]!;
    }


    // public async Task DeleteSessionAsync(string refreshToken)
    // {
    //     var request = new HttpRequestMessage(HttpMethod.Post, $"{CLERK_URI}/v1/client/sessions?__clerk_api_version=2021-02-05&_clerk_js_version=5.34.1&_method=DELETE");
    //     request.Headers.Add("Cookie", $"__client={refreshToken};{TEMP_COOKIES_SET}");
    //     request.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
    //
    //     var response = await HTTP_CLIENT.SendAsync(request);
    //     if (!response.IsSuccessStatusCode)
    //     {
    //         var content = await response.Content.ReadAsStringAsync();
    //         throw new SakuraException($"Failed to log out of the sessions: {GetErrorDescription(content)}", (int)response.StatusCode, HumanizeHttpResponseError(response));
    //     }
    // }


    /// <exception cref="SakuraException"></exception>
    public async Task<SakuraChatResponse> CreateNewChatAsync(string sessionId, string refreshToken, SakuraCharacter character, string firstUserMessage, string locale = "en")
    {
        Refresh();

        var accessToken = await GetAccessTokenAsync(sessionId, refreshToken);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{API_URI_BASE}/chat");
        request.Headers.Add("Cookie", TEMP_COOKIES_SET);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var createNewChatRequest = new RequestCreateNewChat
        {
            context = new InputNewChatContext
            {
                characterId = character.id,
                locale = locale,
                messages =
                [
                    new SakuraMessage
                    {
                        content = character.firstMessage,
                        role = "assistant",
                        type = "text"
                    }
                ]
            },
            action = new SakuraAction
            {
                content = firstUserMessage,
                type = "append"
            }
        };

        request.Content = new StringContent(JsonConvert.SerializeObject(createNewChatRequest), Encoding.UTF8, MediaTypeNames.Application.Json);

        var response = await HTTP_CLIENT.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new SakuraException($"Failed to create new chat: {GetErrorDescription(content)}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var success = content?.Split('\n').Reverse().Any(line => line.Contains("success")) ?? false;
        if (!success)
        {
            throw new SakuraException($"Failed to create new chat: {content}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var start = content!.IndexOf('{');
        var end = content.LastIndexOf('}');
        content = Regex.Unescape(content[start..++end]);

        var chatResponse = JsonConvert.DeserializeObject<SakuraChatResponse>(content)!;
        if (chatResponse.success is false)
        {
            throw new SakuraException($"Failed to create new chat: {content}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        return chatResponse;
    }


    /// <returns>Character response message</returns>
    /// <exception cref="SakuraException"></exception>
    public async Task<SakuraMessage> SendMessageToChatAsync(string sessionId, string refreshToken, string chatId, string message, string locale = "en")
    {
        Refresh();

        var accessToken = await GetAccessTokenAsync(sessionId, refreshToken);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{API_URI_BASE}/chat");
        request.Headers.Add("Cookie", TEMP_COOKIES_SET);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var createNewChatRequest = new RequestSendMessage
        {
            context = new InputMessageContext
            {
                chatId = chatId,
                locale = locale
            },
            action = new SakuraAction
            {
                content = message,
                type = "append"
            }
        };

        request.Content = new StringContent(JsonConvert.SerializeObject(createNewChatRequest), Encoding.UTF8, MediaTypeNames.Application.Json);

        var response = await HTTP_CLIENT.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new SakuraException($"Failed to send message: {GetErrorDescription(content)}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var success = content?.Split('\n').Reverse().Any(line => line.Contains("success")) ?? false;
        if (!success)
        {
            throw new SakuraException($"Failed to send message: {content}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        var unescapedContent = Regex.Unescape(content[start..++end]);

        var chatResponse = JsonConvert.DeserializeObject<SakuraChatResponse>(unescapedContent)!;
        if (chatResponse.success is false)
        {
            throw new SakuraException($"Failed to send message: {content}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        return chatResponse.messages.Last();
    }


    public enum CategoryMatchType { any, all }


    /// <exception cref="SakuraException"></exception>
    public async Task<ICollection<SakuraCharacter>> SearchAsync(string query, bool allowNsfw = true, Category[]? categories = null, CategoryMatchType categoryMatchType = CategoryMatchType.any)
    {
        var urlParams = $"search={query}&allowNsfw={allowNsfw.ToString().ToLower()}";

        if (categories is not null)
        {
            var stringCategories = string.Join(',', categories.Select(CategoryToString));
            urlParams += $"&categories={stringCategories}";

            if (categories.Length > 1)
            {
                urlParams += $"&matchType={categoryMatchType:G}";
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{FRONTEND_URI_BASE}/?{urlParams}");

        var response = await HTTP_CLIENT.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new SakuraException($"Failed to perform search: {GetErrorDescription(content)}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var charactersLine = content.Split('\n').Last(line => line.Contains("{\"characters\"")); // may our Father God
        var jsonLine = charactersLine[charactersLine.IndexOf("{\"characters\"", StringComparison.Ordinal)..]; // forgive me

        var jContent = JsonConvert.DeserializeObject<JObject>(jsonLine)!;
        var characters = JsonConvert.DeserializeObject<List<SakuraCharacter>>(jContent["characters"]!.ToString())!;

        // I hate it
        foreach (var character in characters)
        {
            var props = character.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.PropertyType == typeof(string));

            foreach (var prop in props)
            {
                var stringValue = (string?)prop.GetValue(character);
                if (stringValue is null || !stringValue.StartsWith('$'))
                {
                    continue;
                }

                var newValue = FindValueInRscPayload(stringValue, content);
                if (newValue.Length > 0)
                {
                    prop.SetValue(character, newValue);
                }
            }
        }

        return characters;
    }


    /// <exception cref="SakuraException"></exception>
    public async Task<SakuraCharacter> GetCharacterInfoAsync(string characterId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{FRONTEND_URI_BASE}/chat/{characterId}");

        var response = await HTTP_CLIENT.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new SakuraException($"Failed to get character info: {GetErrorDescription(content)}", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var successLine = content.Split('\n').First(line => line.Contains("\"success\""));
        var jsonLine = successLine[successLine.IndexOf('{')..];

        var jContent = JsonConvert.DeserializeObject<JObject>(jsonLine);
        if (jContent?["success"]?.Value<bool>() is null or false)
        {
            throw new SakuraException("Character not found", (int)response.StatusCode, HumanizeHttpResponseError(response));
        }

        var character = JsonConvert.DeserializeObject<SakuraCharacter>(jContent["data"]!["character"]!.ToString())!;

        var props = character.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.PropertyType == typeof(string));

        foreach (var prop in props)
        {
            var stringValue = (string?)prop.GetValue(character);
            if (stringValue is null || !stringValue.StartsWith('$'))
            {
                continue;
            }

            var newValue = FindValueInRscPayload(stringValue, content);
            if (newValue.Length > 0)
            {
                prop.SetValue(character, newValue);
            }
        }

        return character;
    }


    // Private


    private static string HumanizeHttpResponseError(HttpResponseMessage? response)
    {
        string details;
        if (response is null)
        {
            details = "Failed to get response from SakuraFM";
        }
        else
        {
            details = $"{response.StatusCode:D} ({response.StatusCode:G})\nHeaders: ";

            var headers = response.Headers.ToList();
            if (response.Headers is null || headers.Count == 0)
            {
                details += "none";
            }
            else
            {
                details += string.Join("\n", headers.Select(h => $"[ '{h.Key}'='{h.Value}' ]"));
            }

            var content = Task.Run(async () => await response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(content))
            {
                content = "none";
            }

            details += $"\nContent: {content}";
        }

        return details;
    }


    private static string GetErrorDescription(string? content)
    {
        try
        {
            var sakuraError = JsonConvert.DeserializeObject<SakuraErrorResponse>(content!);
            return sakuraError!.Humanize();
        }
        catch
        {
            return "SOMETHING WENT WRONG";
        }
    }


    private static readonly Regex ANY_RSC_KEY_REGEX = new(@"[\dabcdef]{1,4}\:", RegexOptions.Compiled);
    private static string FindValueInRscPayload(string originalValue, string rscPayload) // kill me
    {
        var CURR_RSC_KEY_WITH_DATA_REGEX = new Regex(@$"{originalValue[1..]}:T.{{0,3}}\,");

        var valueBeginMatch = CURR_RSC_KEY_WITH_DATA_REGEX.Match(rscPayload);
        if (!valueBeginMatch.Success)
        {
            return originalValue;
        }

        var payloadCutLeft = rscPayload[(valueBeginMatch.Index + valueBeginMatch.Length)..];
        var valueEndMatchIndex = ANY_RSC_KEY_REGEX.Match(payloadCutLeft).Index; // always success

        return payloadCutLeft[..valueEndMatchIndex];
    }


    public enum Category
    {
        Male,
        Female,
        Anime,
        MoviesAndTV,
        Yandere,
        Tsundere,
        Gay,
        Lesbian,
        Femboy,
        Futanari,
        VideoGames,
        Furry,
        Horror,
        OC,
        Vampire,
        NonBinary,
        Dominant,
        Submissive,
        MILF,
        DILF,
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


    private void Refresh(bool force = false)
    {
        if (!force && TEMP_COOKIES_SET is not null && _sw.ElapsedMilliseconds < RefreshTimeout)
        {
            return;
        }

        lock (HTTP_CLIENT)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{CLERK_URI}/v1/client");
            var response = HTTP_CLIENT.SendAsync(request).GetAwaiter().GetResult();

            var cookies = response.Headers.Single(h => h.Key.ToLower().StartsWith("set-cookie")).Value;
            TEMP_COOKIES_SET = string.Join(';', cookies);
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

        HTTP_CLIENT.Dispose();

        _disposedValue = true;
    }


    private bool _disposedValue;

    #endregion Dispose
}
