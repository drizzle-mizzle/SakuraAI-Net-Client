namespace SakuraFm.Models.Http;

public class ChatResponse
{
    public string chatId { get; set; }
    public MessageObject[] messages { get; set; }
    public bool success { get; set; }
}
