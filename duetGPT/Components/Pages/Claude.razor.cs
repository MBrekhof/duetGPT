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
        [Inject] private Anthropic Anthropic { get; set; }
        bool running;

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

            string modelChosen = GetModelChosen(ModelValue);
            running = true;

            var userMessage = new Message { Role = Roles.User, Content = textInput };
            var assistantMessage = new Message
            {
                Role = Roles.Assistant,
                Content = "Evaluate your think, let the user know if you do not have enough information to answer."
            };

            try
            {

                chatMessages.Add(userMessage);

                IAsyncEnumerable<IMessageStreamEvent> stream = null;
                try
                {


                 stream = Anthropic.Messages.CreateStreamAsync(new()
                {
                    Model = modelChosen,
                    MaxTokens = 2048,
                    Temperature = temperature,
                    System = string.IsNullOrWhiteSpace(systemInput) ? null : systemInput,
                    Messages = chatMessages.ToArray()
                });
                }
                catch (ClaudiaException ex)
                {
                    Console.WriteLine((int)ex.Status); // 400(ErrorCode.InvalidRequestError)
                    Console.WriteLine(ex.Name);        // invalid_request_error
                    Console.WriteLine(ex.Message);     // Field required. Input:...
                }
                chatMessages.Add(assistantMessage);
                StateHasChanged();

                string markdown = null;

                await foreach (var messageStreamEvent in stream)
                {
                    if (messageStreamEvent is ContentBlockDelta content)
                    {
                        markdown += content.Delta.Text;
                        StateHasChanged();
                    }
                }

                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseSyntaxHighlighting().Build();
                var text = userMessage.Content[0].Text;
                if (text != null)
                    formattedMessages.Add(Markdown.ToHtml(text, pipeline));


                if (markdown != null)
                {
                    formattedMessages.Add(Markdown.ToHtml(markdown, pipeline));
                }
                else
                {
                    formattedMessages.Add(Markdown.ToHtml("Sorry, no response..", pipeline));
                }

                textInput = ""; // clear input.
            }
            finally
            {
                running = false;
            }
        }

        private string GetModelChosen(Model modelValue)
        {
            switch (modelValue)
            {
                case Model.Haiku:
                    return Claudia.Models.Claude3Haiku;
                case Model.Sonnet:
                    return Claudia.Models.Claude3Sonnet;
                case Model.Opus:
                    return Claudia.Models.Claude3Opus;
                default:
                    throw new ArgumentOutOfRangeException(nameof(modelValue),
                        $"Not expected model value: {modelValue}");
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
