using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace duetGPT.Data
{
  public class ExtendedMessageResponse
  {
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public List<ContentItem> Content { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("stop_reason")]
    public string StopReason { get; set; }

    [JsonPropertyName("stop_sequence")]
    public string StopSequence { get; set; }

    [JsonPropertyName("usage")]
    public UsageInfo Usage { get; set; }

    [JsonPropertyName("thinking")]
    public string Thinking { get; set; }

    // Improved method to extract thinking from beta response
    // This will handle different response formats from the API
    public string GetThinkingContent()
    {
      // If thinking is directly available in the response, return it
      if (!string.IsNullOrEmpty(Thinking))
      {
        return Thinking;
      }

      // Check if thinking might be in a special content block
      if (Content != null)
      {
        // Look for any content items that might contain thinking information
        // Some API versions might include thinking as a special content type or in metadata
        foreach (var item in Content)
        {
          if (item.Type == "thinking" || (item.Type == "text" && item.Text?.Contains("<thinking>") == true))
          {
            return item.Text;
          }
        }
      }

      // If we couldn't find thinking content, return an empty string instead of a generic message
      // This allows the UI to properly handle the case where thinking is not available
      return string.Empty;
    }
  }

  public class ContentItem
  {
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }
  }

  public class UsageInfo
  {
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
  }
}