namespace SakuraAi.Models.Common;

public class SakuraCharacter
{
    public string name { get; set; }
    public string description { get; set; }
    public bool nsfw { get; set; }
    public string persona { get; set; }
    public string imageUri { get; set; } // "https://media.sakura.fm/frlOCC6.jpg"
    public string scenario { get; set; }
    public string firstMessage { get; set; }
    public string instructions { get; set; }
    public string genderIdentity { get; set; }
    public List<ExampleConversation> exampleConversation { get; set; }
    public bool truncated { get; set; }
    public string id { get; set; } // "fqDaOBZ"
    public int messageCount { get; set; }
    public DateTime createdAt { get; set; } // "2023-11-04 14:50:09.006572Z"
    public string creatorId { get; set; } // "user_2XcSEBI5iCFW2o8mERBLp41sMjM"
    public string creatorUsername { get; set; }
    public string visibility { get; set; } // "public"
    public dynamic tags { get; set; } // null
    public string[]? categories { get; set; } // [ "Anime", "Tsundere", "Female" ]
    public bool favorited { get; set; }
    public dynamic creatorTier { get; set; } // null
    public dynamic moderationLabels { get; set; } // null
    public string creatorImageUrl { get; set; } // "https://img.clerk.com/eyJ0eXBlIjoicHJveHkiLCJzcmMiOiJodHRwczovL2Nkbi5kaXNjb3JkYXBwLmNvbS9hdmF0YXJzLzkxMTQyNTM5NzIzNTgwMjEyMi9mNzJiYjk4OGNjNTgwZmY2MDcxMmU4NjYyOTNkYTlkMCIsInMiOiJiQVRXMURmcmsvdGZwZGtnWkVBWXI4eC9VSVUwNTlrRFYveDM3VTNvNUc4In0"
    public bool explicitImage { get; set; }
}

public class ExampleConversation
{
    public string role { get; set; }
    public string content { get; set; }
}
