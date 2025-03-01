# Implementing Extended Thinking with Claude 3.7 Sonnet

This document explains how to implement the Extended Thinking feature with Claude 3.7 Sonnet in the duetGPT application.

## Overview

Claude 3.7 Sonnet supports an "extended_thinking" parameter that allows the model to show its reasoning process. This feature helps users understand how Claude arrived at its answers and can be valuable for debugging, education, and transparency.

## Current Implementation Status

The current implementation in duetGPT provides a foundation for using extended thinking, but requires additional integration to fully utilize the feature. The standard Anthropic SDK doesn't directly expose the extended_thinking parameter, so we've created a custom implementation.

## How Extended Thinking Works

When enabled, Claude will return an additional "thinking" field in its response that contains the model's step-by-step reasoning process. This is separate from the final response content and provides insight into how Claude approached the problem.

## Implementation Details

### 1. AnthropicService.cs

We've extended the AnthropicService class to support direct API calls with the extended_thinking parameter:

```csharp
public async Task<ExtendedMessageResponse> SendMessageWithExtendedThinkingAsync(ExtendedMessageRequest request)
{
    try
    {
        var client = GetCustomHttpClient();
        
        // Add the extended_thinking parameter to the request
        request.ExtendedThinking = true;
        
        var response = await client.PostAsJsonAsync("/v1/messages", request);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ExtendedMessageResponse>(content);
        
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error sending message with extended thinking");
        throw;
    }
}
```

### 2. Custom Request and Response Models

We've created custom models to handle the extended thinking parameter and response:

```csharp
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

public class ExtendedMessageResponse
{
    // Other properties...
    
    [JsonPropertyName("thinking")]
    public string Thinking { get; set; }
}
```

## How to Use Extended Thinking

To use extended thinking in the Claude component:

1. Update the `SendClick()` method in Claude.SendMessage.cs to use the custom implementation:

```csharp
if (EnableExtendedThinking && IsExtendedThinkingAvailable())
{
    _logger.LogInformation("Enabling extended thinking for this request");
    
    // Convert standard parameters to extended request
    var extendedRequest = new ExtendedMessageRequest
    {
        Model = modelChosen,
        System = systemPrompt,
        MaxTokens = ModelValue == Model.Sonnet35 ? 8192 : 4096,
        Temperature = 1.0m,
        ExtendedThinking = true
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
    
    // Extract thinking content
    ThinkingContent = extendedResponse.Thinking;
    
    // Create a compatible response object for the rest of the code
    res = new MessageResponse
    {
        Content = extendedResponse.Content.Select(c => new TextContent { Text = c.Text }).Cast<ContentBase>().ToList(),
        Usage = new Usage { InputTokens = extendedResponse.Usage.InputTokens, OutputTokens = extendedResponse.Usage.OutputTokens }
    };
}
```

2. Make sure the UI displays the thinking content:

```html
@if (!string.IsNullOrEmpty(ThinkingContent))
{
    <div class="thinking-content">
        <h3>Claude's Thinking Process</h3>
        <div class="thinking-box">
            @((MarkupString)ThinkingContent)
        </div>
    </div>
}
```

## Limitations

1. The extended thinking feature is only available with Claude 3.7 Sonnet.
2. The standard Anthropic SDK doesn't support this feature directly, requiring our custom implementation.
3. Extended thinking may increase token usage and response times.

## Future Improvements

1. Add support for streaming responses with extended thinking.
2. Improve error handling for the custom API implementation.
3. Add UI controls to toggle the display of thinking content.
4. Consider contributing to the official Anthropic SDK to add native support for extended thinking.

## References

- [Anthropic API Documentation](https://docs.anthropic.com/claude/reference/messages-streaming)
- [Claude 3.7 Sonnet Features](https://www.anthropic.com/news/claude-3-7-sonnet)
