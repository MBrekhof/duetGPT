using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using duetGPT.Data;
using duetGPT.Services;
using DevExpress.Blazor;
using DevExpress.AIIntegration.Blazor.Chat;
using System.Security.Claims;

namespace duetGPT.Components.Pages
{
    public partial class ClaudeV2 : ComponentBase
    {
        #region Injected Services

        [Inject]
        public required ILogger<ClaudeV2> Logger { get; set; }

        [Inject]
        public required Microsoft.Extensions.AI.IChatClient ChatClient { get; set; }

        [Inject]
        public required IChatContextService ChatContext { get; set; }

        [Inject]
        public required IThreadService ThreadService { get; set; }

        [Inject]
        public required IImageService ImageService { get; set; }

        [Inject]
        public required IThreadSummarizationService ThreadSummarizationService { get; set; }

        [Inject]
        public required IDbContextFactory<ApplicationDbContext> DbContextFactory { get; set; }

        [Inject]
        public required AuthenticationStateProvider AuthenticationStateProvider { get; set; }

        [Inject]
        private IToastNotificationService ToastService { get; set; } = default!;

        #endregion

        #region Private Fields

        private DevExpress.AIIntegration.Blazor.Chat.DxAIChat DxAiChat = default!;
        private bool IsProcessing;
        private DuetThread? CurrentThread;
        private string? ImageUrl;
        private string? CurrentImagePath;
        private string? CurrentImageType;
        private int TotalTokens;
        private decimal TotalCost;

        // Model costs per million tokens (MTok)
        private static readonly Dictionary<string, (decimal InputRate, decimal OutputRate)> MODEL_COSTS = new()
        {
            { "claude-haiku-4-5-20251001", (0.001m, 0.005m) },
            { "claude-sonnet-4-5-20250929", (0.003m, 0.015m) },
            { "claude-opus-4-1-20250805", (0.015m, 0.075m) }
        };

        #endregion

        #region Public Properties

        public string ThinkingContent { get; set; } = string.Empty;
        public bool IsThinkingPopupVisible { get; set; } = false;
        public bool IsImagePopupVisible { get; set; } = false;
        public bool IsNewThreadPopupVisible { get; set; } = false;

        public bool EnableWebSearch { get; set; }
        public bool EnableExtendedThinking { get; set; } = false;
        public bool EnableRag { get; set; } = true;

        public List<Document> AvailableFiles { get; set; } = new List<Document>();
        public IEnumerable<int> SelectedFiles { get; set; } = Enumerable.Empty<int>();

        public List<Prompt> Prompts { get; set; } = new List<Prompt>();
        public string? SelectedPrompt { get; set; } = "Default";

        public enum Model
        {
            Sonnet45,
            Haiku45,
            Opus41
        }

        private readonly IEnumerable<Model> _models = Enum.GetValues(typeof(Model)).Cast<Model>();
        private Model ModelValue { get; set; }

        #endregion

        #region Lifecycle Methods

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                Logger.LogInformation("ClaudeV2 component rendered");
                ModelValue = _models.FirstOrDefault();
                await LoadAvailableFiles();
                await LoadPrompts();
                await CreateInitialThread();
                UpdateChatContext(); // Initialize the shared chat context
                StateHasChanged();
            }
        }

        public async Task DisposeAsync()
        {
            await ImageService.ClearImageAsync(CurrentImagePath);
            await ImageService.CleanupTempFolderAsync();
        }

        #endregion

        #region DxAIChat Integration

        // DxAIChat automatically uses the injected IChatClient through DI
        // No manual event handlers needed - it calls GetResponseAsync automatically

        #endregion

        #region Helper Methods

        private void UpdateChatContext()
        {
            // Update the shared context service with current UI state
            ChatContext.SelectedFiles = SelectedFiles;
            ChatContext.ThreadId = CurrentThread?.Id ?? 0;
            ChatContext.CustomPrompt = GetSelectedPromptContent();
            ChatContext.EnableRag = EnableRag;
            ChatContext.EnableWebSearch = EnableWebSearch;
            ChatContext.EnableExtendedThinking = EnableExtendedThinking;
            ChatContext.ModelId = GetModelString(ModelValue);
        }

        private string? GetSelectedPromptContent()
        {
            if (string.IsNullOrEmpty(SelectedPrompt)) return null;
            return Prompts.FirstOrDefault(p => p.Name == SelectedPrompt)?.Content;
        }

        private string GetModelString(Model model)
        {
            return model switch
            {
                Model.Haiku45 => "claude-haiku-4-5-20251001",
                Model.Sonnet45 => "claude-sonnet-4-5-20250929",
                Model.Opus41 => "claude-opus-4-1-20250805",
                _ => "claude-sonnet-4-5-20250929"
            };
        }

        private bool IsExtendedThinkingAvailable()
        {
            return ModelValue == Model.Sonnet45 || ModelValue == Model.Opus41;
        }

        private (decimal InputRate, decimal OutputRate) GetModelCosts(string modelId)
        {
            return MODEL_COSTS.TryGetValue(modelId, out var costs)
                ? costs
                : MODEL_COSTS["claude-sonnet-4-5-20250929"];
        }

        private static decimal CalculateCost(int tokens, decimal ratePerMTok)
        {
            return (tokens / 1_000_000m) * ratePerMTok;
        }

        #endregion

        #region Thread Management

        private async Task CreateInitialThread()
        {
            try
            {
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    Logger.LogWarning("No user ID found");
                    return;
                }

                CurrentThread = await ThreadService.CreateThreadAsync(userId, SelectedPrompt);
                TotalTokens = 0;
                TotalCost = 0;
                Logger.LogInformation("Created new thread {ThreadId}", CurrentThread.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error creating initial thread");
            }
        }

        private void ShowNewThreadConfirmation()
        {
            IsNewThreadPopupVisible = true;
        }

        private async Task ConfirmNewThread()
        {
            IsNewThreadPopupVisible = false;

            // Clear the DxAIChat component's message history
            if (DxAiChat != null)
            {
                var emptyMessages = new List<DevExpress.AIIntegration.Blazor.Chat.BlazorChatMessage>();
                DxAiChat.LoadMessages(emptyMessages);
            }

            await ClearImageData();
            await CreateInitialThread();

            // Reset UI state
            TotalTokens = 0;
            TotalCost = 0;

            StateHasChanged();
        }

        private async Task GenerateThreadTitle(string assistantResponse)
        {
            if (CurrentThread == null) return;

            try
            {
                await using var dbContext = await DbContextFactory.CreateDbContextAsync();

                // Get the user's last message
                var userMessage = await dbContext.Messages
                    .Where(m => m.ThreadId == CurrentThread.Id && m.Role == "user")
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync();

                if (userMessage == null) return;

                // Generate title (simplified - you could use IChatClient here too)
                var title = $"Chat - {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
                if (userMessage.Content.Length > 50)
                {
                    title = userMessage.Content.Substring(0, 50) + "...";
                }

                CurrentThread.Title = title;
                dbContext.Update(CurrentThread);
                await dbContext.SaveChangesAsync();

                Logger.LogInformation("Thread title generated: {Title}", title);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error generating thread title");
            }
        }

        #endregion

        #region Image Handling

        private async Task HandleImageUpload(InputFileChangeEventArgs e)
        {
            try
            {
                var file = e.File;
                if (file != null)
                {
                    var result = await ImageService.HandleImageUploadAsync(file);
                    CurrentImagePath = result.TempFilePath;
                    CurrentImageType = result.ImageType;
                    ImageUrl = result.DisplayDataUrl;
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error uploading image");
                ToastService.ShowToast(new ToastOptions()
                {
                    ProviderName = "ClaudeV2Page",
                    ThemeMode = ToastThemeMode.Dark,
                    RenderStyle = ToastRenderStyle.Danger,
                    Title = "Upload Error",
                    Text = $"Failed to upload image: {ex.Message}"
                });
                await ClearImageData();
            }
        }

        private async Task ClearImageData()
        {
            await ImageService.ClearImageAsync(CurrentImagePath);
            ImageUrl = null;
            CurrentImagePath = null;
            CurrentImageType = null;
            IsImagePopupVisible = false;
            await InvokeAsync(StateHasChanged);
        }

        private void ShowImagePopup()
        {
            IsImagePopupVisible = true;
        }

        #endregion

        #region Summarization

        private async Task SummarizeThread()
        {
            if (CurrentThread == null)
            {
                ToastService.ShowToast(new ToastOptions()
                {
                    ProviderName = "ClaudeV2Page",
                    ThemeMode = ToastThemeMode.Dark,
                    RenderStyle = ToastRenderStyle.Danger,
                    Title = "Error",
                    Text = "No active thread to summarize"
                });
                return;
            }

            try
            {
                IsProcessing = true;
                StateHasChanged();

                // Get current user
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    ToastService.ShowToast(new ToastOptions()
                    {
                        ProviderName = "ClaudeV2Page",
                        ThemeMode = ToastThemeMode.Dark,
                        RenderStyle = ToastRenderStyle.Danger,
                        Title = "Error",
                        Text = "User not authenticated"
                    });
                    return;
                }

                // Load messages and summarize
                var messages = await ThreadService.LoadThreadMessagesAsync(CurrentThread.Id);
                var modelId = GetModelString(ModelValue);
                await ThreadSummarizationService.SummarizeAndSaveAsync(
                    CurrentThread, messages, modelId, userId);

                ToastService.ShowToast(new ToastOptions()
                {
                    ProviderName = "ClaudeV2Page",
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
                    ProviderName = "ClaudeV2Page",
                    ThemeMode = ToastThemeMode.Dark,
                    RenderStyle = ToastRenderStyle.Danger,
                    Title = "Error",
                    Text = "Error saving summary"
                });
            }
            finally
            {
                IsProcessing = false;
                StateHasChanged();
            }
        }

        #endregion

        #region Data Loading

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
            }
        }

        private async Task LoadAvailableFiles()
        {
            try
            {
                Logger.LogInformation("Loading available files");
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                await using var dbContext = await DbContextFactory.CreateDbContextAsync();
                if (userId != null)
                {
                    AvailableFiles = await dbContext.Documents
                        .Include(d => d.Owner)
                        .Where(d => d.OwnerId == userId)
                        .ToListAsync();
                    Logger.LogInformation("Loaded {Count} available files", AvailableFiles.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading available files");
            }
        }

        #endregion
    }
}
