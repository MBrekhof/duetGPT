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

    [JsonPropertyName("thinking")]
    public ThinkingParameters Thinking { get; set; }
  }

  public class ThinkingParameters
  {
    [JsonPropertyName("budget_tokens")]
    public int BudgetTokens { get; set; }
  }

  public class MessageItem
  {
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
  }
}