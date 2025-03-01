# Implementation Plan for Extended Thinking with Claude 3.7 Sonnet

## Overview

This document outlines the step-by-step plan to implement the Extended Thinking feature with Claude 3.7 Sonnet in the duetGPT application. The feature allows users to see Claude's reasoning process, providing transparency and insight into how the AI arrives at its answers.

## Implementation Steps

### 1. Create Custom Request and Response Models

We need to create custom models to handle the extended_thinking parameter and response:

- Create `ExtendedMessageRequest.cs` in the Data folder
- Create `ExtendedMessageResponse.cs` in the Data folder

### 2. Extend AnthropicService

We need to extend the AnthropicService class to support direct API calls with the extended_thinking parameter:

- Add a method to create a custom HTTP client
- Implement the `SendMessageWithExtendedThinkingAsync` method

### 3. Update Claude Component

We need to update the Claude component to use the extended thinking feature:

- Add a property to store the thinking content
- Add a toggle to enable/disable extended thinking
- Update the `SendClick()` method to use the custom implementation when extended thinking is enabled

### 4. Add UI Elements

We need to add UI elements to display the thinking content:

- Add a toggle switch in the sidebar
- Add a section to display the thinking content when available

### 5. Add CSS Styling

We need to add CSS styling for the new UI elements:

- Style the thinking content section
- Style the toggle switch

## Detailed Implementation

### 1. Create Custom Request and Response Models

#### ExtendedMessageRequest.cs
```csharp
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
```

#### ExtendedMessageResponse.cs
```csharp
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
```

### 2. Extend AnthropicService

Update AnthropicService.cs to include the following methods:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using duetGPT.Data;

// Add to existing AnthropicService class

private HttpClient GetCustomHttpClient()
{
    var apiKey = _configuration["Anthropic:ApiKey"];
    if (string.IsNullOrEmpty(apiKey))
    {
        _logger.LogError("Anthropic API key is not configured");
        throw new ArgumentException("Anthropic API key is not configured.");
    }

    var client = new HttpClient();
    client.BaseAddress = new Uri("https://api.anthropic.com");
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Add("x-api-key", apiKey);
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

    return client;
}

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

### 3. Update Claude Component

#### Add properties to Claude.razor.cs

```csharp
// Add to the Claude class
public string ThinkingContent { get; set; }
public bool EnableExtendedThinking { get; set; } = false;

private bool IsExtendedThinkingAvailable()
{
    // Only available for Claude 3.7 Sonnet
    return ModelValue == Model.Sonnet37;
}
```

#### Update SendClick() method in Claude.SendMessage.cs

Modify the SendClick() method to use the custom implementation when extended thinking is enabled:

```csharp
// Add this code block inside the SendClick() method, before the standard Anthropic API call
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
else
{
    // Existing code for standard API call
    // ...
}
```

### 4. Update Claude.razor UI

Add a toggle for extended thinking in the sidebar:

```html
<!-- Add to the sidebar info-box in Claude.razor -->
<div class="toggle-container">
    <DxCheckBox @bind-Checked="@EnableExtendedThinking" />
    <span>Enable Extended Thinking</span>
    @if (EnableExtendedThinking && !IsExtendedThinkingAvailable())
    {
        <div class="warning-text">Only available with Claude 3.7 Sonnet</div>
    }
</div>
```

Add a section to display the thinking content:

```html
<!-- Add after the messages-container div in Claude.razor -->
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

### 5. Add CSS Styling

Add CSS styling for the new UI elements:

```css
/* Add to the style section in Claude.razor */
.thinking-content {
    margin-top: 20px;
    padding: 15px;
    background-color: #f8f9fa;
    border-radius: 5px;
    border: 1px solid #dee2e6;
    width: 100%;
}

.thinking-box {
    background-color: #ffffff;
    padding: 15px;
    border-radius: 5px;
    border: 1px solid #dee2e6;
    margin-top: 10px;
    white-space: pre-wrap;
    overflow-x: auto;
}

.warning-text {
    color: #dc3545;
    font-size: 0.8rem;
    margin-top: 5px;
}
```

## File Modifications Summary

Here's a summary of all the files that need to be created or modified:

### New Files
1. `duetGPT/Data/ExtendedMessageRequest.cs` - Custom request model for extended thinking
2. `duetGPT/Data/ExtendedMessageResponse.cs` - Custom response model for extended thinking

### Modified Files
1. `duetGPT/Services/AnthropicService.cs` - Add methods for custom HTTP client and extended thinking API call
2. `duetGPT/Components/Pages/Claude.razor.cs` - Add properties for extended thinking
3. `duetGPT/Components/Pages/Claude.SendMessage.cs` - Update SendClick method to use extended thinking
4. `duetGPT/Components/Pages/Claude.razor` - Add UI elements for extended thinking toggle and display

## Implementation Considerations

1. **API Compatibility**: The extended_thinking parameter is only available with Claude 3.7 Sonnet, so we need to ensure it's only enabled when this model is selected.

2. **Error Handling**: We need to handle errors gracefully if the extended thinking API call fails, falling back to the standard API call.

3. **Performance**: Extended thinking may increase token usage and response times, so we should make this clear to users.

4. **UI/UX**: The thinking content should be displayed in a way that's easy to read and understand, with proper formatting and styling.

5. **Testing**: We need to thoroughly test the feature with various types of queries to ensure it works correctly and provides useful insights.

## Testing Plan

1. Verify that the extended thinking toggle only enables when Claude 3.7 Sonnet is selected
2. Test sending a message with extended thinking enabled
3. Verify that the thinking content is displayed correctly
4. Test with various types of queries to ensure the thinking content is useful
5. Verify that the feature can be toggled on and off without issues

## Future Improvements

1. Add support for streaming responses with extended thinking
2. Improve error handling for the custom API implementation
3. Add UI controls to toggle the display of thinking content
4. Consider contributing to the official Anthropic SDK to add native support for extended thinking

## Next Steps

To implement this plan, we need to switch to Code mode to create and modify the necessary files. The implementation should follow the steps outlined in this document, with careful attention to error handling and testing.