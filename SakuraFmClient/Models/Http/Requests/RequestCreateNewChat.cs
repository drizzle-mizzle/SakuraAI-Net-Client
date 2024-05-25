namespace SakuraFmClient.Models.Http;

public class RequestCreateNewChat
{
    public ActionObject action { get; set; }
    public InputNewChatContext context { get; set; }
}
