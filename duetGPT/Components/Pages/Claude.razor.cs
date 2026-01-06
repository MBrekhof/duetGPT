using Anthropic.SDK.Messaging;
using duetGPT.Data;
using duetGPT.Services;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using DevExpress.Blazor;
using System.Security.Claims;

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
        /// Service for managing chat messages and AI interactions.
        /// </summary>
        [Inject]
        public required IChatMessageService ChatMessageService { get; set; }

        /// <summary>
        /// Service for managing thread operations.
        /// </summary>
        [Inject]
        public required IThreadService ThreadService { get; set; }

        /// <summary>
        /// Service for managing image uploads and processing.
        /// </summary>
        [Inject]
        public required IImageService ImageService { get; set; }

        /// <summary>
        /// Service for thread summarization.
        /// </summary>
        [Inject]
        public required IThreadSummarizationService ThreadSummarizationService { get; set; }

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

        /// <summary>
        /// Toast notification service for user feedback.
        /// </summary>
        [Inject]
        private IToastNotificationService ToastService { get; set; } = default!;

        #endregion

        #region Private Fields

        private string textInput = "";
        private List<Message> chatMessages = new();
        private List<SystemMessage> systemMessages = new();
        private bool running;
        private bool newThread = false;
        private DuetThread? currentThread;

        // Messages formatted for display
        private List<string> formattedMessages = new();

        // Image handling
        private string? ImageUrl { get; set; }
        private string? CurrentImagePath { get; set; }
        private string? CurrentImageType { get; set; }

        #endregion

        #region Public Properties

        /// <summary>
        /// Content of Claude's thinking process when extended thinking is enabled
        /// </summary>
        public string ThinkingContent { get; set; } = string.Empty;

        /// <summary>
        /// Flag to enable or disable extended thinking
        /// </summary>
        public bool EnableExtendedThinking { get; set; } = false;

        /// <summary>
        /// Flag to enable or disable web search
        /// </summary>
        public bool EnableWebSearch { get; set; }

        /// <summary>
        /// Flag to control visibility of the thinking content popup
        /// </summary>
        public bool IsThinkingPopupVisible { get; set; } = false;

        /// <summary>
        /// Flag to control visibility of the image preview popup
        /// </summary>
        public bool IsImagePopupVisible { get; set; } = false;

        /// <summary>
        /// Flag to control visibility of the new thread confirmation popup
        /// </summary>
        public bool IsNewThreadPopupVisible { get; set; } = false;

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
                _tokens = value;
                StateHasChanged();
            }
        }

        private decimal _cost;
        public decimal Cost
        {
            get => _cost;
            set
            {
                _cost = value;
                StateHasChanged();
            }
        }

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

        public async Task DisposeAsync()
        {
            await ImageService.ClearImageAsync(CurrentImagePath);
            await ImageService.CleanupTempFolderAsync();
        }

        #endregion

        #region UI Event Handlers

        /// <summary>
        /// Shows the image popup
        /// </summary>
        private void ShowImagePopup()
        {
            IsImagePopupVisible = true;
            StateHasChanged();
        }

        /// <summary>
        /// Shows the new thread confirmation popup
        /// </summary>
        private void ShowNewThreadConfirmation()
        {
            IsNewThreadPopupVisible = true;
            StateHasChanged();
        }

        /// <summary>
        /// Confirms creating a new thread and clears current state
        /// </summary>
        private async Task ConfirmNewThread()
        {
            IsNewThreadPopupVisible = false;
            await ClearImageData();
            await ClearThread();
        }

        #endregion

        #region Message Handling

        /// <summary>
        /// Handles the send message click event and processes the user's message through the AI service
        /// </summary>
        async Task SendClick()
        {
            if (string.IsNullOrWhiteSpace(textInput))
            {
                return;
            }

            try
            {
                running = true;
                await InvokeAsync(StateHasChanged);

                // Create thread if it doesn't exist yet
                if (currentThread == null)
                {
                    currentThread = await CreateNewThread();
                    newThread = true;
                }
                else if (string.IsNullOrEmpty(currentThread.Title) || currentThread.Title == "Not yet created")
                {
                    newThread = true;
                }

                // Load existing messages from database if we have a thread and chatMessages is empty
                if (currentThread != null && !chatMessages.Any())
                {
                    await LoadMessagesFromDb();
                }

                var modelChosen = GetModelChosen(ModelValue);

                // Get system prompt
                string systemPrompt = @"You are an expert at analyzing user questions and providing accurate, relevant answers.
Use the following guidelines:
1. Prioritize information from the provided knowledge base when available
2. Supplement with your general knowledge when needed
3. Clearly indicate when you're using provided knowledge versus general knowledge
4. If the provided knowledge seems insufficient or irrelevant, rely on your general expertise
5. Ultrathink and Ultracheck your answer before answering";

                await using var dbContext = await DbContextFactory.CreateDbContextAsync();
                if (!string.IsNullOrEmpty(SelectedPrompt))
                {
                    var selectedPromptContent = await dbContext.Set<Prompt>()
                        .Where(p => p.Name == SelectedPrompt)
                        .Select(p => p.Content)
                        .FirstOrDefaultAsync();

                    if (!string.IsNullOrEmpty(selectedPromptContent))
                    {
                        systemPrompt = selectedPromptContent;
                    }
                }

                // Get image bytes if available
                var imageBytes = await ImageService.GetImageBytesAsync(CurrentImagePath ?? string.Empty);

                // Send message using service
                var request = new SendMessageRequest
                {
                    UserInput = textInput,
                    Thread = currentThread,
                    Model = modelChosen,
                    SystemPrompt = systemPrompt,
                    SelectedFileIds = SelectedFiles,
                    ImageBytes = imageBytes,
                    ImageType = CurrentImageType,
                    EnableWebSearch = EnableWebSearch,
                    EnableExtendedThinking = EnableExtendedThinking,
                    ChatHistory = chatMessages
                };

                var result = await ChatMessageService.SendMessageAsync(request);

                // Update chat messages
                chatMessages.Add(new Message(RoleType.User, textInput, null));
                chatMessages.Add(new Message(RoleType.Assistant, result.AssistantResponse, null));

                // Update UI display
                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                formattedMessages.Add(Markdown.ToHtml(textInput, pipeline));
                formattedMessages.Add(Markdown.ToHtml(result.AssistantResponse, pipeline));

                // Update thinking content if available
                if (!string.IsNullOrEmpty(result.ThinkingContent))
                {
                    ThinkingContent = result.ThinkingContent;
                }

                // Update tokens and cost
                Tokens += result.InputTokens + result.OutputTokens;
                Cost += result.InputCost + result.OutputCost;

                await ThreadService.UpdateThreadMetricsAsync(currentThread, Tokens, Cost);

                // Generate thread title if this is a new thread
                if (newThread)
                {
                    var title = await ChatMessageService.GenerateThreadTitleAsync(
                        textInput, result.AssistantResponse, modelChosen);

                    await using var titleDbContext = await DbContextFactory.CreateDbContextAsync();
                    currentThread.Title = title;
                    titleDbContext.Update(currentThread);
                    await titleDbContext.SaveChangesAsync();

                    newThread = false;
                }

                textInput = "";
                Logger.LogInformation("Message sent and processed successfully");
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Network error while communicating with AI service");
                ToastService.ShowToast(new ToastOptions()
                {
                    ProviderName = "ClaudePage",
                    ThemeMode = ToastThemeMode.Dark,
                    RenderStyle = ToastRenderStyle.Danger,
                    Title = "Network Error",
                    Text = "Error communicating with AI service. Please check your network connection and try again."
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing message in SendClick");
                ToastService.ShowToast(new ToastOptions()
                {
                    ProviderName = "ClaudePage",
                    ThemeMode = ToastThemeMode.Dark,
                    RenderStyle = ToastRenderStyle.Danger,
                    Title = "Processing Error",
                    Text = "An error occurred while processing your message. Please try again."
                });
            }
            finally
            {
                running = false;
                StateHasChanged();
            }
        }

        #endregion

        #region Thread Management

        /// <summary>
        /// Creates a new thread
        /// </summary>
        private async Task<DuetThread> CreateNewThread()
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userId == null)
            {
                Logger.LogWarning("Attempted to create a new thread without a valid user ID");
                throw new InvalidOperationException("User ID is required to create a new thread");
            }

            var thread = await ThreadService.CreateThreadAsync(userId, SelectedPrompt);

            // Initialize system messages (for UI state)
            systemMessages = new List<SystemMessage>();
            chatMessages = new List<Message>();
            formattedMessages = new List<string>();

            return thread;
        }

        /// <summary>
        /// Clears the current thread and creates a new one
        /// </summary>
        private async Task ClearThread()
        {
            try
            {
                Logger.LogInformation("Clearing thread");
                chatMessages.Clear();
                formattedMessages.Clear();
                Tokens = 0;
                Cost = 0;
                SelectedFiles = Enumerable.Empty<int>();

                currentThread = await CreateNewThread();
                newThread = true;
                Logger.LogInformation("Thread cleared and new thread created with ID {ThreadId}", currentThread.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error clearing thread");
                throw;
            }
        }

        /// <summary>
        /// Loads messages from the database for the current thread
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

        #endregion

        #region Image Handling

        /// <summary>
        /// Handles image upload
        /// </summary>
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
                    ProviderName = "ClaudePage",
                    ThemeMode = ToastThemeMode.Dark,
                    RenderStyle = ToastRenderStyle.Danger,
                    Title = "Upload Error",
                    Text = $"Failed to upload image: {ex.Message}"
                });
                await ClearImageData();
            }
        }

        /// <summary>
        /// Clears image data
        /// </summary>
        private async Task ClearImageData()
        {
            await ImageService.ClearImageAsync(CurrentImagePath);

            ImageUrl = null;
            CurrentImagePath = null;
            CurrentImageType = null;
            IsImagePopupVisible = false;
            await InvokeAsync(StateHasChanged);
        }

        #endregion

        #region Summarization

        /// <summary>
        /// Summarizes the current thread and saves to knowledge base
        /// </summary>
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
                var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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

                var modelChosen = GetModelChosen(ModelValue);
                await ThreadSummarizationService.SummarizeAndSaveAsync(
                    currentThread, chatMessages, modelChosen, userId);

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

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if extended thinking is available for the current model
        /// </summary>
        private bool IsExtendedThinkingAvailable()
        {
            return ModelValue == Model.Sonnet45 || ModelValue == Model.Haiku45 || ModelValue == Model.Opus41;
        }

        /// <summary>
        /// Gets the model string based on the selected model enum value
        /// </summary>
        private string GetModelChosen(Model modelValue)
        {
            try
            {
                return modelValue switch
                {
                    Model.Haiku45 => "claude-haiku-4-5-20251001",
                    Model.Sonnet45 => "claude-sonnet-4-5-20250929",
                    Model.Opus41 => "claude-opus-4-1-20250805",
                    _ => "claude-sonnet-4-5-20250929"
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error getting model chosen");
                return "claude-sonnet-4-5-20250929";
            }
        }

        /// <summary>
        /// Loads available prompts from the database
        /// </summary>
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

        /// <summary>
        /// Loads available files for the current user
        /// </summary>
        private async Task LoadAvailableFiles()
        {
            try
            {
                Logger.LogInformation("Loading available files");
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var user = authState.User;
                var currentUser = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

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

        #endregion
    }
}
