namespace SakuraAi.Client.Models.Http.Responses;

public class ChatResponse
{
    public string chatId { get; set; }
    public MessageObject[] messages { get; set; }
    public bool success { get; set; }
}
