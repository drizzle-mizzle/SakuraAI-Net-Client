namespace SakuraFmClient.Models.Http;

public class RequestSendMessage
{
    public ActionObject action { get; set; }
    public InputMessageContext context { get; set; }
}
