using Claudia;
using Microsoft.AspNetCore.Components;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        double temperature = 1.0;
        string textInput = "";
        string systemInput = SystemPrompts.Claude3;
        List<Message> chatMessages = new();
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
                var response = currentMessage.Content[0].Text;
                running = false;
            }
        }
    }
}
