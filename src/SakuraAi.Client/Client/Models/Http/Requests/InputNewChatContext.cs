﻿namespace SakuraAi.Client.Models.Http.Requests;

public class InputNewChatContext
{
    public string characterId { get; set; }
    public string locale { get; set; }
    public MessageObject[]? messages { get; set; }
}
