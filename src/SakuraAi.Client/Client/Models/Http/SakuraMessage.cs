namespace SakuraAi.Client.Models.Http;

public class SakuraMessage
{
    public string content { get; set; }
    public string role { get; set; }
    public string type { get; set; }

    // In responses only
    public string id { get; set; }
}
