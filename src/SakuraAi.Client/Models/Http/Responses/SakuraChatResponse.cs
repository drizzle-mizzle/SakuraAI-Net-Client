namespace SakuraAi.Client.Models.Http.Responses;

public class SakuraChatResponse
{
    public string chatId { get; set; }
    public SakuraMessage[] messages { get; set; }
    public bool success { get; set; }
}
