namespace SakuraAi.Client.Models.Http.Requests;

internal class InputNewChatContext
{
    public string characterId { get; set; }
    public string locale { get; set; }
    public SakuraMessage[]? messages { get; set; }
}
