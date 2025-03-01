using Anthropic.SDK.Messaging;
using duetGPT.Data;
using duetGPT.Services;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Tavily;

namespace duetGPT.Components.Pages
{
    /// <summary>
    /// Component for handling Claude AI chat interactions and message management.
    /// </summary>
    public partial class Claude : ComponentBase
    {
        #region Injected Services

        /// <summary>
        /// Logger instance for the Claude component.
        /// </summary>
        [Inject]
        public required ILogger<Claude> Logger { get; set; }

        /// <summary>
        /// Service for interacting with the Anthropic AI API.
        /// </summary>
        [Inject]
        public required AnthropicService AnthropicService { get; set; }

        /// <summary>
        /// Service for managing knowledge base operations.
        /// </summary>
        [Inject]
        public required IKnowledgeService KnowledgeService { get; set; }

        /// <summary>
        /// Factory for creating database contexts.
        /// </summary>
        [Inject]
        public required IDbContextFactory<ApplicationDbContext> DbContextFactory { get; set; }

        /// <summary>
        /// Provider for authentication state management.
        /// </summary>
        [Inject]
        public required AuthenticationStateProvider AuthenticationStateProvider { get; set; }

        /// <summary>
        /// Application configuration provider.
        /// </summary>
        [Inject]
        public required IConfiguration Configuration { get; set; }

        #endregion

        private string textInput = "";
        private List<Message> chatMessages = new();
        private List<SystemMessage> systemMessages = new();
        private bool running;
        private bool newThread = false;
        private DuetThread? currentThread;
        private bool EnableWebSearch { get; set; }

        // Messages formatted for display
        private List<string> formattedMessages = new();

        /// <summary>
        /// Content of Claude's thinking process when extended thinking is enabled
        /// </summary>
        public string ThinkingContent { get; set; } = string.Empty;

        /// <summary>
        /// Flag to enable or disable extended thinking
        /// </summary>
        public bool EnableExtendedThinking { get; set; } = false;

        /// <summary>
        /// Checks if extended thinking is available for the current model
        /// </summary>
        /// <returns>True if extended thinking is available, false otherwise</returns>
        private bool IsExtendedThinkingAvailable()
        {
            // Only available for Claude 3.7 Sonnet
            return ModelValue == Model.Sonnet37;
        }

        /// <summary>
        /// Loads messages from the database for the current thread and converts them to Anthropic Message format
        /// </summary>
        private async Task LoadMessagesFromDb()
        {
            if (currentThread == null) return;

            chatMessages.Clear();

            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            var dbMessages = await dbContext.Messages
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
            set
            {
                InvokeAsync(async () => await UpdateTokensAsync(value));
            }
        }

        private decimal _cost;
        public decimal Cost
        {
            get => _cost;
            set
            {
                InvokeAsync(async () => await UpdateCostAsync(value));
            }
        }

        public enum Model
        {
            Sonnet37,
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
                await using var dbContext = await DbContextFactory.CreateDbContextAsync();
                Prompts = await dbContext.Set<Prompt>().ToListAsync();
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

                await using var dbContext = await DbContextFactory.CreateDbContextAsync();
                if (currentUser != null)
                {
                    AvailableFiles = await dbContext.Documents
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
