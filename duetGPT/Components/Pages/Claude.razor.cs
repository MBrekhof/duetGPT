using Claudia;
using Markdig;
using Markdig.SyntaxHighlighting;
using Microsoft.AspNetCore.Components;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        double temperature = 1.0;
        string textInput = "";

        string systemInput = SystemPrompts.Claude3; // <--TODO: to replace with variable prompt
        List<Message> chatMessages = new();
        private List<String> formattedMessages = new();
        [Inject]
        private Anthropic Anthropic { get; set; }
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
        
        async Task SendClick()
        {
            string modelChosen = "Claude3Haiku";
            if (running) return;
            if (string.IsNullOrWhiteSpace(textInput)) return;
            modelChosen = "Claude3" + ModelValue.ToString();
            running = true;
            
            var userMessage = new Message { Role = Roles.User, Content = textInput };
            var assistantMessage = new Message { Role = Roles.Assistant, Content = "Evaluate your think, let the user know if you do not have enough information to answer." };
            
            try
            {

                chatMessages.Add(userMessage);
                chatMessages.Add(assistantMessage);
                

                 var responseMessage = await Anthropic.Messages.CreateAsync(new()
                {
                    Model = Claudia.Models.Claude3Haiku,
                    MaxTokens = 2048,
                    Temperature = temperature,
                    System = string.IsNullOrWhiteSpace(systemInput) ? null : systemInput,
                    
                    Messages = chatMessages.ToArray()
                });
                
                StateHasChanged();
                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseSyntaxHighlighting().Build();
                var text = userMessage.Content[0].Text;
                if (text != null)
                    formattedMessages.Add(Markdown.ToHtml(text, pipeline));
                
                string markdown = null;
                if (responseMessage.Content != null && responseMessage.Content.Any())
                {
                    markdown = responseMessage.Content[0].Text;
                    if (markdown != null)
                    {
                        formattedMessages.Add(Markdown.ToHtml(markdown, pipeline));
                    }
                }
                else
                {
                    formattedMessages.Add(Markdown.ToHtml("Sorry, no response..", pipeline));
                }
                textInput = ""; // clear input.

                // await foreach (var messageStreamEvent in stream)
                // {
                //     if (messageStreamEvent is ContentBlockDelta content)
                //     {
                //
                //         assistantMessage.Content[0].Text += content.Delta.Text;
                //
                //         StateHasChanged();
                //     }
                // }
            }
            finally
            {
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
