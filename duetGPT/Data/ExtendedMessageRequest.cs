using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace duetGPT.Data
{
  public class ExtendedMessageRequest
  {
    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("messages")]
    public List<MessageItem> Messages { get; set; }

    [JsonPropertyName("system")]
    public string System { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public decimal Temperature { get; set; }

    [JsonPropertyName("extended_thinking")]
    public bool ExtendedThinking { get; set; }
  }

  public class MessageItem
  {
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
  }
}