namespace SakuraAi.Client.Models.Http.Requests;

internal class RequestSendMessage
{
    public SakuraAction action { get; set; }
    public InputMessageContext context { get; set; }
}
