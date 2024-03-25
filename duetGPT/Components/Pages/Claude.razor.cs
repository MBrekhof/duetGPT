using Claudia;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        double temperature = 1.0;
        string textInput = "";
        private string htmlContent = "";
        string systemInput = SystemPrompts.Claude3;
        List<Message> chatMessages = new();
        private List<String> formattedMessages = new();
        [Inject]
        private Anthropic Anthropic { get; set; }
        [Inject]
        private IJSRuntime JSRuntime { get; set; }
        
        bool running = false;

        public enum Model
        {
            Haiku,
            Sonnet,
            Opus
        }

        private readonly IEnumerable<Model> _models = Enum.GetValues(typeof(Model)).Cast<Model>();

        private Model ModelValue { get; set; }

        protected override void OnInitialized()
        {
            ModelValue = _models.FirstOrDefault();
        }

        private ElementReference textareaRef;

        private void AdjustTextareaHeight()
        {
            _ = AdjustTextareaHeightAsync();
        }

        private async Task AdjustTextareaHeightAsync()
        {
            await JSRuntime.InvokeVoidAsync("adjustTextareaHeight", textareaRef);
        }

        async Task SendClick()
        {
            string modelChosen = "Claude3Haiku";
            if (running) return;
            if (string.IsNullOrWhiteSpace(textInput)) return;
            modelChosen = "Claude3" + ModelValue.ToString();
            running = true;
            var currentMessage = new Message { Role = Roles.Assistant, Content = "" };
            try
            {
                chatMessages.Add(new() { Role = Roles.User, Content = textInput });

                var stream = Anthropic.Messages.CreateStreamAsync(new()
                {
                    Model = Claudia.Models.Claude3Haiku,
                    MaxTokens = 1024,
                    Temperature = temperature,
                    System = string.IsNullOrWhiteSpace(systemInput) ? null : systemInput,
                    Messages = chatMessages.ToArray()
                });
                
                chatMessages.Add(currentMessage);


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
                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                var markdown = currentMessage.Content[0].Text;
                if (markdown != null)
                    formattedMessages.Add(Markdown.ToHtml(markdown, pipeline));
                formattedMessages.Add(Markdown.ToHtml(textInput, pipeline));
                textInput = ""; // clear input.
                running = false;
            }
        }

        private Task ClearThread()
        {
            chatMessages.Clear();
            formattedMessages.Clear();
            return Task.CompletedTask;
        }
    }
}
