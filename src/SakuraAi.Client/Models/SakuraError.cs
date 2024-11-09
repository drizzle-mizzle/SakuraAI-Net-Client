using Newtonsoft.Json;

namespace SakuraAi.Client.Models;


public class SakuraErrorResponse
{
    public List<SakuraError>? errors { get; set; }
    public string? clerk_trace_id { get; set; }


    public virtual string Humanize()
    {
        if (errors is null || errors.Count == 0)
        {
            return clerk_trace_id ?? "SOMETHING WENT WRONG";
        }

        var stringErrors = errors.Select(JsonConvert.SerializeObject);
        return $"Error: {string.Join(", ", stringErrors)}";
    }
}


public class SakuraError
{
    public string? message { get; set; }
    public string? long_message { get; set; }
    public string? code { get; set; }
}
