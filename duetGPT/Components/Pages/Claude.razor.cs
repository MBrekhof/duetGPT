using Anthropic.SDK.Messaging;
using duetGPT.Data;
using duetGPT.Services;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using DevExpress.Pdf;
using DevExpress.XtraRichEdit;
using System.Text;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        [Inject] private ILogger<Claude> Logger { get; set; }
        [Inject] private AnthropicService AnthropicService { get; set; }
        [Inject] private ApplicationDbContext DbContext { get; set; }
        [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; }
        [Inject] private ErrorPopupService ErrorService { get; set; }

        string textInput = "";
        List<Message> chatMessages = new();
        List<Message> userMessages = new();
        List<SystemMessage> systemMessages = new();
        Message assistantMessage = new();
        private List<String> formattedMessages = new();
        bool running;

        private DuetThread currentThread;

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
            try
            {
                Logger.LogInformation("Initializing Claude component");
                //ModelValue = _models.FirstOrDefault();
                //await LoadAvailableFiles();
                //await LoadPrompts();
                //await CreateNewThread();  // Changed to await the async call
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during component initialization");
                throw;
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
                await CreateNewThread();  // Changed to await the async call

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
