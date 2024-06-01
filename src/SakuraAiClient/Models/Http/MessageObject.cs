namespace SakuraAi.Models.Http;

public class MessageObject
{
    public string content { get; set; }
    public string role { get; set; }
    public string type { get; set; }

    // In responses only
    public string id { get; set; }
}
