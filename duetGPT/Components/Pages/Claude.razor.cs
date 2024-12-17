using Anthropic.SDK.Messaging;
using duetGPT.Data;
using duetGPT.Services;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        [Inject] private ILogger<Claude> Logger { get; set; }
        [Inject] private AnthropicService AnthropicService { get; set; }
        [Inject] private ApplicationDbContext DbContext { get; set; }
        [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; }


        private string textInput = "";
        private List<Message> chatMessages = new();
        private List<SystemMessage> systemMessages = new();
        private bool running;
        private bool newThread = false;
        private DuetThread currentThread;

        // Messages formatted for display
        private List<string> formattedMessages = new();

        /// <summary>
        /// Loads messages from the database for the current thread and converts them to Anthropic Message format
        /// </summary>
        private async Task LoadMessagesFromDb()
        {
            if (currentThread == null) return;

            chatMessages.Clear();

            var dbMessages = await DbContext.Messages
                .Where(m => m.ThreadId == currentThread.Id)
                .OrderBy(m => m.Id)
                .ToListAsync();

            foreach (var msg in dbMessages)
            {
                var role = msg.Role == "user" ? RoleType.User : RoleType.Assistant;
                chatMessages.Add(new Message(role, msg.Content, null));
            }

            // Rebuild formatted messages for display
            formattedMessages.Clear();
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            foreach (var msg in dbMessages)
            {
                formattedMessages.Add(Markdown.ToHtml(msg.Content, pipeline));
            }
        }

        public List<Document> AvailableFiles { get; set; } = new List<Document>();
        public IEnumerable<int> SelectedFiles { get; set; } = Enumerable.Empty<int>();

        // Added for Prompts support
        public List<Prompt> Prompts { get; set; } = new List<Prompt>();
        public string? SelectedPrompt { get; set; } = "Default";

        private int _tokens;
        public int Tokens
        {
            get => _tokens;
            set => UpdateTokensAsync(value);
        }

        private decimal _cost;
        public decimal Cost
        {
            get => _cost;
            set => UpdateCostAsync(value);
        }

        public enum Model
        {
            Sonnet35,
            Haiku35,
            Sonnet,
            Opus
        }

        private readonly IEnumerable<Model> _models = Enum.GetValues(typeof(Model)).Cast<Model>();

        private Model ModelValue { get; set; }

        protected override async Task OnInitializedAsync()
        {
            if (currentThread != null)
            {
                await LoadMessagesFromDb();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            base.OnAfterRender(firstRender);
            if (firstRender)
            {
                Logger.LogInformation("Claude component rendered");
                ModelValue = _models.FirstOrDefault();
                await LoadAvailableFiles();
                await LoadPrompts();
                StateHasChanged();
            }
        }

        private async Task LoadPrompts()
        {
            try
            {
                Logger.LogInformation("Loading prompts");
                Prompts = await DbContext.Set<Prompt>().ToListAsync();
                Logger.LogInformation("Loaded {Count} prompts", Prompts.Count);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading prompts");
                throw;
            }
        }

        private async Task LoadAvailableFiles()
        {
            try
            {
                Logger.LogInformation("Loading available files");
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var user = authState.User;
                var currentUser = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                AvailableFiles = await DbContext.Documents.ToListAsync();
                if (currentUser != null)
                {
                    AvailableFiles = await DbContext.Documents
                        .Include(d => d.Owner)
                        .Where(d => d.OwnerId == currentUser)
                        .ToListAsync();
                    Logger.LogInformation("Loaded {Count} available files", AvailableFiles.Count);
                }
            }
            catch (DbUpdateException ex)
            {
                Logger.LogError(ex, "Database error while loading available files");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error while loading available files");
                throw;
            }
        }

        // Other methods are moved to separate files
    }
}
