using Anthropic.SDK.Messaging;
using DevExpress.Blazor;
using DevExpress.Blazor.Internal;
using duetGPT.Services;

namespace duetGPT.Components.Pages
{
    public partial class Claude
  {
    private async Task SummarizeThread()
    {
      if (currentThread == null || !chatMessages.Any())
      {
        ToastService.ShowToast(new ToastOptions()
        {
          ProviderName = "ClaudePage",
          ThemeMode = ToastThemeMode.Dark,
          RenderStyle = ToastRenderStyle.Danger,
          Title = "Error",
          Text = "No messages to summarize"
        });
        return;
      }

      try
      {
        running = true;
        StateHasChanged();

        // Get current user
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var userId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
          ToastService.ShowToast(new ToastOptions()
          {
            ProviderName = "ClaudePage",
            ThemeMode = ToastThemeMode.Dark,
            RenderStyle = ToastRenderStyle.Danger,
            Title = "Error",
            Text = "User not authenticated"
          });
          return;
        }

        // Create prompt for summarization
        var threadContent = string.Join("\n\n", chatMessages.Select(m =>
        {
          var content = m.Content is List<ContentBase> contentList
              ? string.Join(" ", contentList.OfType<TextContent>().Select(tc => tc.Text))
              : m.Content?.ToString() ?? "";
          return $"{m.Role}: {content}";
        }));
        var summarizationPrompt = $"Please provide a concise summary of the following conversation, highlighting the key points and conclusions:\n\n{threadContent}";

        // Get summary from Claude
        var client = AnthropicService.GetAnthropicClient();
        var messages = new List<Message>
            {
                new Message(RoleType.User, summarizationPrompt)
            };

        var parameters = new MessageParameters
        {
          Messages = messages,
          MaxTokens = 1024,
          Model = GetModelChosen(ModelValue),
          Stream = false,
          Temperature = 0.7m
        };

        var response = await client.Messages.GetClaudeMessageAsync(parameters);
        var summary = response.Content[0].ToString();

        // Save to knowledge base
        var metadata = $"type:chat_summary;source:thread_{currentThread.Id};date:{DateTime.UtcNow:yyyy-MM-dd}";
        // Ensure title stays within 50 character limit
        var baseTitle = currentThread.Title ?? $"Thread {currentThread.Id}";
        var title = $"Summary - {baseTitle}";
        if (title.Length > 50)
        {
          // Truncate the base title to fit within limits, accounting for "Chat Summary - " (14 chars) and ellipsis (3 chars)
          var maxBaseTitleLength = 42; 
          title = $"Chat - {baseTitle.Substring(0, maxBaseTitleLength)}";
        }

        await KnowledgeService.SaveKnowledgeAsync(summary, title, metadata, userId);

        ToastService.ShowToast(new ToastOptions()
        {
          ProviderName = "ClaudePage",
          ThemeMode = ToastThemeMode.Dark,
          RenderStyle = ToastRenderStyle.Success,
          Title = "Success",
          Text = "Thread summary saved to knowledge base"
        });
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Error summarizing thread");
        ToastService.ShowToast(new ToastOptions()
        {
          ProviderName = "ClaudePage",
          ThemeMode = ToastThemeMode.Dark,
          RenderStyle = ToastRenderStyle.Danger,
          Title = "Error",
          Text = "Error saving summary"
        });
      }
      finally
      {
        running = false;
        StateHasChanged();
      }
    }
  }
}
