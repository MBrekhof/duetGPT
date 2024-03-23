using Claudia;
using Microsoft.AspNetCore.Components;

namespace duetGPT.Components.Pages;
public partial class Claude
{
    [Inject]
    public required Anthropic Anthropic { get; init; }

    double temperature = 1.0;
    string textInput = "";
    string systemInput = SystemPrompts.Claude3;
    List<Message> chatMessages = new();

    bool running = false;

    async Task SendClick()
    {
        string modelChosen = "Claude3Haiku";
        if (running) return;
        if (string.IsNullOrWhiteSpace(textInput)) return;
        modelChosen = "Claude3"+ModelValue.ToString();
        running = true;
        try
        {
            chatMessages.Add(new() { Role = Roles.User, Content = textInput });

            var stream = Anthropic.Messages.CreateStreamAsync(new()
            {
                Model = modelChosen,
                MaxTokens = 1024,
                Temperature = temperature,
                System = string.IsNullOrWhiteSpace(systemInput) ? null : systemInput,
                Messages = chatMessages.ToArray()
            });

            var currentMessage = new Message { Role = Roles.Assistant, Content = "" };
            chatMessages.Add(currentMessage);

            textInput = ""; // clear input.
            StateHasChanged();

            await foreach (var messageStreamEvent in stream)
            {
                if (messageStreamEvent is ContentBlockDelta content)
                {
                    currentMessage.Content[0].Text += content.Delta.Text;
                    StateHasChanged();
                }
            }
        }
        finally
        {
            running = false;
        }
    }
}

