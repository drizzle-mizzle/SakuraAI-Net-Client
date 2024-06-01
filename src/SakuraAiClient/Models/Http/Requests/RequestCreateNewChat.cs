namespace SakuraAi.Models.Http.Requests;

public class RequestCreateNewChat
{
    public ActionObject action { get; set; }
    public InputNewChatContext context { get; set; }
}
