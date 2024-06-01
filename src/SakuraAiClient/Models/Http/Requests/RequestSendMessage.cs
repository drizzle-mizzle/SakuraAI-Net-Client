namespace SakuraAi.Models.Http.Requests;

public class RequestSendMessage
{
    public ActionObject action { get; set; }
    public InputMessageContext context { get; set; }
}
