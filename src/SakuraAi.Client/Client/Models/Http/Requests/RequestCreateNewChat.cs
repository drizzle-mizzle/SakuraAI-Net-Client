namespace SakuraAi.Client.Models.Http.Requests;

internal class RequestCreateNewChat
{
    public SakuraAction action { get; set; }
    public InputNewChatContext context { get; set; }
}
