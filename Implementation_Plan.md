# Implementation Plan: Fixing Extended Thinking for Claude 3.7 Sonnet

## Problem Statement

The Extended Thinking feature for Claude 3.7 Sonnet is not functioning correctly in the duetGPT application. When attempting to use this feature, the application receives a 400 Bad Request error from the Anthropic API. The error occurs in `AnthropicService.SendMessageWithExtendedThinkingAsync()` on line 127.

## Root Cause Analysis

After examining the code and comparing it with the example provided, I've identified the following issues:

1. **Request Format Mismatch**: The current implementation uses a boolean flag `extended_thinking: true`, but the Anthropic API expects a structured `thinking` parameter with a `budget_tokens` property.

2. **API Version Compatibility**: The code is using the beta header `anthropic-beta: thinking-2024-03-01`, which may be correct but needs to be used with the proper request format.

3. **Response Handling**: The current implementation may not correctly handle the thinking content in the response.

## Implementation Plan

### 1. Update the ExtendedMessageRequest Class

The request model needs to be updated to match the expected format by the Anthropic API:

```csharp
// duetGPT/Data/ExtendedMessageRequest.cs
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
```

### 2. Update the ExtendedMessageResponse Class

Ensure the response model can properly handle the thinking content:

```csharp
// duetGPT/Data/ExtendedMessageResponse.cs
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

    [JsonPropertyName("thinking_content")]
    public List<ThinkingContentItem> ThinkingContent { get; set; }

    // Improved method to extract thinking from response
    public string GetThinkingContent()
    {
      // If thinking is directly available in the response, return it
      if (!string.IsNullOrEmpty(Thinking))
      {
        return Thinking;
      }

      // Check if thinking might be in the thinking_content list
      if (ThinkingContent != null && ThinkingContent.Count > 0)
      {
        return string.Join("\n", ThinkingContent.Select(tc => tc.Text));
      }

      // Check if thinking might be in a special content block
      if (Content != null)
      {
        // Look for any content items that might contain thinking information
        foreach (var item in Content)
        {
          if (item.Type == "thinking" || (item.Type == "text" && item.Text?.Contains("<thinking>") == true))
          {
            return item.Text;
          }
        }
      }

      // If we couldn't find thinking content, return an empty string
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

  public class ThinkingContentItem
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
```

### 3. Update the AnthropicService

Modify the `SendMessageWithExtendedThinkingAsync` method to use the new structure:

```csharp
// duetGPT/Services/AnthropicService.cs
public async Task<ExtendedMessageResponse> SendMessageWithExtendedThinkingAsync(ExtendedMessageRequest request)
{
    try
    {
        _logger.LogInformation("Sending message with extended thinking enabled");
        var client = GetCustomHttpClient();

        // Create a modified request object that includes the thinking parameter
        var requestJson = new
        {
            model = request.Model,
            messages = request.Messages,
            system = request.System,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            thinking = request.Thinking  // Use the structured thinking parameter
        };

        // Serialize the request manually
        var jsonContent = JsonSerializer.Serialize(requestJson);
        _logger.LogDebug("Request JSON: {Json}", jsonContent);

        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        // Log the request headers for debugging
        foreach (var header in client.DefaultRequestHeaders)
        {
            _logger.LogDebug("Request header: {Key} = {Value}", header.Key, string.Join(", ", header.Value));
        }

        var response = await client.PostAsync("/v1/messages", content);

        // Log the response status code
        _logger.LogDebug("Response status code: {StatusCode}", response.StatusCode);

        // Read the response content even if it's an error
        var responseContent = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Response content: {Content}", responseContent);

        // Now check if the response was successful
        response.EnsureSuccessStatusCode();

        var result = JsonSerializer.Deserialize<ExtendedMessageResponse>(responseContent);

        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize response from Anthropic API");
        }

        // Log whether thinking content was found
        var thinkingContent = result.GetThinkingContent();
        if (string.IsNullOrEmpty(thinkingContent))
        {
            _logger.LogWarning("No thinking content found in the response.");
        }
        else
        {
            _logger.LogInformation("Thinking content found with length: {Length}", thinkingContent.Length);
        }

        _logger.LogInformation("Successfully received extended thinking response");
        return result;
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "HTTP error sending message with extended thinking: {Message}", ex.Message);
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error sending message with extended thinking: {Message}", ex.Message);
        throw;
    }
}
```

### 4. Update the Claude.SendMessage.cs File

Modify the `SendClick()` method to use the new request format:

```csharp
// duetGPT/Components/Pages/Claude.SendMessage.cs
if (EnableExtendedThinking && IsExtendedThinkingAvailable())
{
    _logger.LogInformation("Enabling extended thinking for this request");

    try
    {
        // Convert standard parameters to extended request
        var extendedRequest = new ExtendedMessageRequest
        {
            Model = modelChosen,
            System = systemPrompt,
            MaxTokens = ModelValue == Model.Sonnet35 ? 8192 : 4096,
            Temperature = 1.0m,
            Thinking = new ThinkingParameters
            {
                BudgetTokens = 16000  // Allocate 16,000 tokens for thinking
            }
        };

        // Convert messages to the format expected by the custom API
        extendedRequest.Messages = chatMessages.Concat(new[] { message })
            .Select(m => new MessageItem
            {
                Role = m.Role.ToString().ToLower(),
                Content = m.Content is List<ContentBase> contentList
                      ? string.Join("\n", contentList.OfType<TextContent>().Select(tc => tc.Text))
                      : m.Content.ToString()
            })
            .ToList();

        // Call the custom API
        var extendedResponse = await _anthropicService.SendMessageWithExtendedThinkingAsync(extendedRequest);

        // Extract the response
        markdown = string.Join("\n", extendedResponse.Content.Select(c => c.Text));

        // Extract thinking content using the helper method
        var thinkingContent = extendedResponse.GetThinkingContent();
        if (!string.IsNullOrEmpty(thinkingContent))
        {
            ThinkingContent = thinkingContent;
            _logger.LogInformation("Received thinking content: {Length} characters", ThinkingContent.Length);
        }
        else
        {
            _logger.LogWarning("No thinking content was found in the response");
            ThinkingContent = "Extended thinking was requested but not returned by the model. This may be due to API limitations or the specific query type.";
        }

        // Create a compatible response object for the rest of the code
        res = new MessageResponse
        {
            Content = extendedResponse.Content.Select(c => new TextContent { Text = c.Text }).Cast<ContentBase>().ToList(),
            Usage = new Usage { InputTokens = extendedResponse.Usage.InputTokens, OutputTokens = extendedResponse.Usage.OutputTokens }
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error using extended thinking feature. Falling back to standard API.");
        // Fall back to standard API
        useStandardApi = true;
    }
}
```

### 5. Update the IsExtendedThinkingAvailable Method

Ensure the method correctly identifies models that support extended thinking:

```csharp
// duetGPT/Components/Pages/Claude.razor.cs
private bool IsExtendedThinkingAvailable()
{
    // Currently, only Claude 3.7 Sonnet supports extended thinking
    return ModelValue == Model.Sonnet37;
}
```

## Testing Strategy

1. **Basic Functionality Test**:
   - Send a simple message with extended thinking enabled
   - Verify the request format in logs
   - Confirm thinking content is received and displayed

2. **Error Handling Test**:
   - Test with invalid parameters
   - Verify fallback to standard API works correctly

3. **Model Compatibility Test**:
   - Test with Claude 3.7 Sonnet (should work)
   - Test with other models (should fall back to standard API)

4. **Token Budget Test**:
   - Test with different token budget values
   - Monitor token usage and costs

## Implementation Timeline

1. **Day 1**: Update the request and response models
2. **Day 2**: Modify the AnthropicService and Claude.SendMessage.cs
3. **Day 3**: Testing and debugging
4. **Day 4**: Documentation and final adjustments

## Success Criteria

1. Extended thinking feature works correctly with Claude 3.7 Sonnet
2. No 400 Bad Request errors when using the feature
3. Thinking content is properly displayed in the UI
4. Fallback to standard API works when needed

## Risks and Mitigations

1. **API Changes**: Anthropic may update their API, requiring further adjustments
   - Mitigation: Monitor Anthropic's API documentation for changes

2. **Token Usage**: Extended thinking may significantly increase token usage and costs
   - Mitigation: Implement configurable token budgets and usage warnings

3. **Performance Impact**: Extended thinking may increase response times
   - Mitigation: Add UI indicators for when extended thinking is in progress

## Future Improvements

1. Add support for streaming responses with extended thinking
2. Create a UI toggle for enabling/disabling extended thinking
3. Add visualization tools for the thinking process
4. Implement token budget configuration in the UI