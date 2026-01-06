# duetGPT Modernization Implementation Plan

## Overview
Comprehensive refactoring to: (1) migrate PostgreSQL to Docker with pgvector, (2) replace custom chat UI with DevExpress DxAIChat, and (3) extract business logic from Claude.razor into services.

**Total Estimated Time:** 15-20 hours
**User Preferences:** Automatic DB restore, adapter wrapper pattern, extract services only, preserve RAG/documents/prompts

## Progress Summary

### ✅ Phase 1: COMPLETED (January 5, 2026)
- **Duration**: ~2 hours
- **PostgreSQL Version**: Upgraded from 15.x to 18.1
- **pgvector Version**: 0.8.1
- **Database**: 14 tables, 272 MB backup successfully restored
- **Container**: duetgpt-postgres (healthy, running on localhost:5432)
- **Files Created**:
  - `docker-compose.yml`
  - `scripts/backup-database.ps1`
  - `scripts/migrate-to-docker.ps1`
  - `scripts/migrate-to-docker-alternative.ps1`

### ⏳ Phase 2: Service Extraction - PENDING

### ⏳ Phase 3: IChatClient Adapter - PENDING

### ⏳ Phase 4: DxAIChat Integration - PENDING

---

## Phase 1: Docker PostgreSQL Migration ✅ COMPLETED

### Current State
- Local PostgreSQL 15.x at localhost:5432
- Database: duetgpt, User: postgres
- 31 migrations, pgvector extension with 1536-dim vectors
- Auto-migration on startup via `context.Database.Migrate()`

### Implementation Steps

#### 1.1 Create Docker Infrastructure

**File:** `docker-compose.yml`
```yaml
version: '3.8'
services:
  postgres:
    image: pgvector/pgvector:pg18  # Updated to PostgreSQL 18
    container_name: duetgpt-postgres
    environment:
      POSTGRES_DB: duetgpt
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: 1Zaqwsx2
      PGDATA: /var/lib/postgresql/data/pgdata
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./db-backup:/docker-entrypoint-initdb.d
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres-data:
```

**File:** `scripts/backup-database.ps1`
```powershell
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupFile = "db-backup/duetgpt_backup_$timestamp.sql"

& "C:\Program Files\PostgreSQL\15\bin\pg_dump.exe" `
  -h localhost -U postgres -d duetgpt -F p -f $backupFile

if ($LASTEXITCODE -eq 0) {
    Copy-Item $backupFile "db-backup/01-restore.sql" -Force
    Write-Host "✓ Backup created: $backupFile"
}
```

#### 1.2 Migration Process
1. Stop application
2. Run `.\scripts\backup-database.ps1`
3. Verify `db-backup/01-restore.sql` exists
4. Start Docker: `docker-compose up -d`
5. Wait for health check: `docker-compose ps`
6. Test connection: `psql -h localhost -U postgres -d duetgpt`
7. Start application: `dotnet run`
8. Verify: Login, view threads, send message, RAG query

**Rollback:** Stop Docker, start local PostgreSQL (connection string unchanged)

**Files Modified:**
- Create: `docker-compose.yml`, `scripts/backup-database.ps1`
- Optional: `appsettings.json` (connection string already correct)

---

## Phase 2: Service Extraction from Claude.razor (4-5 hours)

### Current State
**Claude.razor: 1,664 lines across 9 files**
- Claude.razor (173L) - UI markup
- Claude.razor.cs (258L) - Component logic
- **Claude.SendMessage.cs (638L)** - MASSIVE message flow ⚠️
- Claude.ThreadManagement.cs (203L) - Thread CRUD
- Claude.ImageHandling.cs (160L) - Image uploads
- Claude.Summarize.cs (117L) - Summarization
- Claude.UpdateMethods.cs (75L) - DB updates
- Claude.Helpers.cs (40L) - Text extraction

### Services to Create

#### 2.1 IChatMessageService
**File:** `Services/ChatMessageService.cs`

**Responsibilities:**
- Orchestrate entire message send flow (from Claude.SendMessage.cs lines 54-515)
- Call IKnowledgeService for RAG context
- Process attached documents via IDocumentService
- Build system prompt with context
- Call AnthropicService API
- Handle extended thinking, tool calls, streaming
- Save messages to database
- Calculate tokens/costs
- Generate thread titles (lines 524-584)

**Interface:**
```csharp
public interface IChatMessageService
{
    Task<SendMessageResult> SendMessageAsync(SendMessageRequest request);
    Task<string> GenerateThreadTitleAsync(string userMsg, string assistantMsg, string model);
}

public record SendMessageRequest
{
    public required string UserInput { get; init; }
    public required DuetThread Thread { get; init; }
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public IEnumerable<int> SelectedFileIds { get; init; } = [];
    public byte[]? ImageBytes { get; init; }
    public string? ImageType { get; init; }
    public bool EnableWebSearch { get; init; }
    public bool EnableExtendedThinking { get; init; }
    public List<Message> ChatHistory { get; init; } = new();
}

public record SendMessageResult
{
    public required string AssistantResponse { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required decimal InputCost { get; init; }
    public required decimal OutputCost { get; init; }
    public string? ThinkingContent { get; init; }
    public required Message UserMessage { get; init; }
    public required Message AssistantMessage { get; init; }
}
```

#### 2.2 IThreadService
**File:** `Services/ThreadService.cs`

**Responsibilities:**
- Create new threads (Claude.ThreadManagement.cs lines 10-78)
- Load thread messages from DB
- Associate documents with threads (lines 103-156)
- Get thread document contents (lines 158-201)
- Update thread metrics (Claude.UpdateMethods.cs lines 7-74)

**Interface:**
```csharp
public interface IThreadService
{
    Task<DuetThread> CreateThreadAsync(string userId, string? selectedPrompt = null);
    Task<List<Message>> LoadThreadMessagesAsync(int threadId);
    Task AssociateDocumentsWithThreadAsync(DuetThread thread, IEnumerable<int> docIds);
    Task<List<string>> GetThreadDocumentContentsAsync(DuetThread thread);
    Task UpdateThreadMetricsAsync(DuetThread thread, int tokens, decimal cost);
}
```

#### 2.3 IImageService
**File:** `Services/ImageService.cs`

**Responsibilities:**
- Handle image upload with validation (Claude.ImageHandling.cs lines 26-85)
- Save to temp folder with unique filename
- Create resized preview for UI
- Retrieve original bytes (lines 109-126)
- Cleanup temp files (lines 87-107, 141-158)

**Interface:**
```csharp
public interface IImageService
{
    Task<ImageUploadResult> HandleImageUploadAsync(IBrowserFile file);
    Task<byte[]?> GetImageBytesAsync(string imagePath);
    Task ClearImageAsync(string? imagePath);
    Task CleanupTempFolderAsync();
}

public record ImageUploadResult
{
    public required string TempFilePath { get; init; }
    public required string ImageType { get; init; }
    public required string DisplayDataUrl { get; init; }
}
```

#### 2.4 IThreadSummarizationService
**File:** `Services/ThreadSummarizationService.cs`

**Responsibilities:**
- Build summarization prompt from chat history (Claude.Summarize.cs lines 10-115)
- Call Anthropic API for summary
- Format metadata
- Save to knowledge base via IKnowledgeService

**Interface:**
```csharp
public interface IThreadSummarizationService
{
    Task<SummarizationResult> SummarizeAndSaveAsync(
        DuetThread thread, List<Message> chatMessages, string model, string userId);
}

public record SummarizationResult
{
    public required string Summary { get; init; }
    public required Knowledge SavedKnowledge { get; init; }
}
```

### Service Registration

**Modify:** `Program.cs` (after line 72)
```csharp
// Add chat services
builder.Services.AddScoped<IChatMessageService, ChatMessageService>();
builder.Services.AddScoped<IThreadService, ThreadService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IThreadSummarizationService, ThreadSummarizationService>();

// Add DocumentProcessingService (currently missing from DI)
builder.Services.AddScoped<DocumentProcessingService>();
```

### Refactored Claude.razor.cs

**Result:** ~200 lines (down from 258 + 638 + 203 + 160 + 117 + 75 + 40 = 1,491 lines)

**Key Changes:**
- Replace direct API calls with `ChatMessageService.SendMessageAsync()`
- Replace thread management with `ThreadService` methods
- Replace image handling with `ImageService` methods
- Replace summarization with `SummarizationService.SummarizeAndSaveAsync()`
- Keep UI state management and event handlers in component

**Files to Delete After Refactor:**
- `Claude.SendMessage.cs`
- `Claude.ThreadManagement.cs`
- `Claude.ImageHandling.cs`
- `Claude.Summarize.cs`
- `Claude.UpdateMethods.cs`
- `Claude.Helpers.cs` (duplicates DocumentProcessingService)

---

## Phase 3: IChatClient Adapter (3-4 hours)

### Architecture

DevExpress DxAIChat uses **Microsoft.Extensions.AI** `IChatClient` interface. Create adapter wrapper around existing AnthropicService.

**NuGet Packages:**
- `DevExpress.AIIntegration.Blazor.Chat` v25.2.3
- `Microsoft.Extensions.AI` v9.0.1

### Adapter Implementation

**File:** `Services/AnthropicChatClientAdapter.cs`

**Key Design:**
- Implements `IChatClient` interface
- Wraps `AnthropicService.GetAnthropicClient()`
- Injects RAG/documents/prompts via `ChatOptions.AdditionalProperties`
- Converts between `Microsoft.Extensions.AI.ChatMessage` and `Anthropic.SDK.Message`
- Supports extended thinking, tool calls, streaming

**Context Injection Keys:**
```csharp
private const string KEY_SELECTED_FILES = "selectedFiles";
private const string KEY_THREAD_ID = "threadId";
private const string KEY_CUSTOM_PROMPT = "customPrompt";
private const string KEY_ENABLE_RAG = "enableRag";
private const string KEY_ENABLE_WEBSEARCH = "enableWebSearch";
private const string KEY_ENABLE_THINKING = "enableExtendedThinking";
```

**Core Methods:**
```csharp
public async Task<ChatCompletion> CompleteAsync(
    IList<ChatMessage> chatMessages,
    ChatOptions? options = null,
    CancellationToken cancellationToken = default)
{
    // 1. Extract context from options.AdditionalProperties
    // 2. Build system prompt with RAG + documents
    // 3. Convert messages to Anthropic SDK format
    // 4. Call AnthropicClient.Messages.GetClaudeMessageAsync
    // 5. Handle tool calls if present (second API call)
    // 6. Convert response back to IChatClient format
}

public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(...)
{
    // Similar flow with StreamClaudeMessageAsync
}
```

**System Prompt Building:**
```csharp
private async Task<string> BuildSystemPromptAsync(string userQuery, RequestContext context)
{
    var prompt = context.CustomPrompt ?? "You are a helpful assistant...";
    var knowledgeContent = new List<string>();

    // Add RAG context if enabled
    if (context.EnableRag)
    {
        var relevantKnowledge = await _knowledgeService.GetRelevantKnowledgeAsync(userQuery);
        knowledgeContent.AddRange(relevantKnowledge.Select(k => k.Content));
    }

    // Add document content if files selected
    if (context.SelectedFiles.Any())
    {
        var thread = await LoadThreadAsync(context.ThreadId);
        var docs = await _threadService.GetThreadDocumentContentsAsync(thread);
        knowledgeContent.AddRange(docs);
    }

    if (knowledgeContent.Any())
        prompt += "\n\nRelevant knowledge:\n" + string.Join("\n---\n", knowledgeContent);

    return prompt;
}
```

### Registration

**Modify:** `Program.cs` (after service registrations)
```csharp
// Register IChatClient adapter
builder.Services.AddScoped<IChatClient>(sp =>
{
    var anthropicService = sp.GetRequiredService<AnthropicService>();
    var knowledgeService = sp.GetRequiredService<IKnowledgeService>();
    var threadService = sp.GetRequiredService<IThreadService>();
    var logger = sp.GetRequiredService<ILogger<AnthropicChatClientAdapter>>();

    return new AnthropicChatClientAdapter(
        anthropicService,
        knowledgeService,
        threadService,
        logger,
        modelId: "claude-sonnet-4-5-20250929");
});
```

---

## Phase 4: DxAIChat Integration (4-5 hours)

### Migration Strategy: Parallel Implementation

**Create new page:** `ClaudeV2.razor` at `/chat-v2` route
**Keep existing:** `Claude.razor` at `/claude` route

**Benefits:**
- Existing UI continues working
- Easy A/B testing
- Simple rollback (change routing)
- Gradual user migration

### Component Structure

**File:** `Components/Pages/ClaudeV2.razor`

**Core DxAIChat Setup:**
```razor
<DxAIChat @ref="aiChat"
          ChatClient="@chatClient"
          UseStreaming="@EnableStreaming"
          FileUploadEnabled="false"
          MessageSent="OnMessageSent"
          ResponseReceived="OnResponseReceived"
          CssClass="duet-ai-chat">
</DxAIChat>
```

**Sidebar Preserves:**
- Model selection (DxComboBox)
- Prompt selection (DxComboBox)
- Document selection (DxListBox)
- RAG toggle (DxCheckBox)
- Extended thinking toggle (DxCheckBox)
- Web search toggle (DxCheckBox)
- Token/cost display
- Thread info
- Image upload (with workaround)

### Event Handlers

**File:** `Components/Pages/ClaudeV2.razor.cs`

```csharp
private IChatClient CreateConfiguredChatClient()
{
    return new ConfiguredChatClientWrapper(
        ChatClient,
        GetChatOptions());
}

private ChatOptions GetChatOptions()
{
    return new ChatOptions
    {
        ModelId = GetModelString(SelectedModel),
        Temperature = 1.0f,
        MaxOutputTokens = 16384,
        AdditionalProperties = new Dictionary<string, object>
        {
            ["selectedFiles"] = SelectedFiles,
            ["threadId"] = currentThread?.Id ?? 0,
            ["customPrompt"] = GetSelectedPromptContent(),
            ["enableRag"] = EnableRag,
            ["enableWebSearch"] = EnableWebSearch,
            ["enableExtendedThinking"] = EnableExtendedThinking
        }
    };
}

private async Task OnMessageSent(AIMessengerMessageSentEventArgs args)
{
    // 1. Ensure thread exists
    // 2. Save user message to database
    // 3. Update chatClient configuration (in case settings changed)
}

private async Task OnResponseReceived(AIMessengerResponseReceivedEventArgs args)
{
    // 1. Save assistant message to database
    // 2. Update token/cost metrics
    // 3. Update thread in database
    // 4. Refresh UI
}
```

### Feature Preservation

| Feature | Status | Implementation |
|---------|--------|---------------|
| RAG integration | ✅ | Via adapter AdditionalProperties |
| Document attachments | ✅ | Via adapter AdditionalProperties |
| Custom prompts | ✅ | Via adapter AdditionalProperties |
| Extended thinking | ✅ | Via adapter ThinkingParameters |
| Web search | ✅ | Via adapter Tools configuration |
| Streaming | ✅ | DxAIChat UseStreaming property |
| Thread management | ✅ | OnMessageSent/OnResponseReceived |
| Summarization | ✅ | Existing button + service |
| Image upload | ⚠️ | Limited - no vision API in DxAIChat yet |

**Image Upload Workaround:**
- Store path in AdditionalProperties
- Inject into system prompt: "[User attached image: {filename}]"
- Alternative: Pre-process with vision API, pass description as text

### Navigation Update

**Modify:** `Components/Layout/NavMenu.razor`
```razor
<DxMenuItem Text="Chat (Modern)" NavigateUrl="/chat-v2" IconCssClass="oi oi-chat" />
<DxMenuItem Text="Chat (Classic)" NavigateUrl="/claude" IconCssClass="oi oi-chat" />
```

### Cutover Plan

**Week 1:** Deploy both UIs, default to `/claude`, test `/chat-v2`
**Week 2-3:** Gather feedback, fix bugs, ensure feature parity
**Week 4:** Change default route to `/chat-v2`, keep `/claude-classic` for rollback
**After Stability:** Deprecate classic UI

---

## Testing Checklist

### Phase 1: Docker PostgreSQL ✅ COMPLETED
- [x] Container starts healthy
- [x] pgvector extension enabled (v0.8.1)
- [x] Migrations applied (14 tables restored)
- [x] Application builds successfully
- [x] Data restored correctly (272 MB backup)
- [ ] Vector search returns results (to be tested on first run)
- [ ] New messages save (to be tested on first run)

### Phase 2: Service Extraction
- [ ] ChatMessageService sends messages
- [ ] RAG context injected
- [ ] Documents processed
- [ ] Extended thinking captured
- [ ] Tool calls execute
- [ ] ThreadService creates threads
- [ ] Thread metrics updated
- [ ] ImageService uploads images
- [ ] Temp files cleaned up
- [ ] SummarizationService saves summaries
- [ ] Claude.razor still works

### Phase 3: IChatClient Adapter
- [ ] Basic chat works
- [ ] RAG context injected
- [ ] Documents included
- [ ] Custom prompts applied
- [ ] Extended thinking enabled
- [ ] Web search executes
- [ ] Streaming works
- [ ] Token usage tracked
- [ ] Errors handled

### Phase 4: DxAIChat Integration
- [ ] ClaudeV2.razor loads
- [ ] Messages sent/received
- [ ] Streaming works smoothly
- [ ] Database saves messages
- [ ] Thread created automatically
- [ ] File selection updates context
- [ ] Prompt selection changes behavior
- [ ] Token/cost tracking accurate
- [ ] New thread clears chat
- [ ] Summarization works

---

## File Modification Summary

### CREATE (17 files)

**Docker:**
1. `docker-compose.yml`
2. `scripts/backup-database.ps1`
3. `scripts/validate-migration.ps1`
4. `db-backup/` (folder)

**Services:**
5. `Services/IChatMessageService.cs`
6. `Services/ChatMessageService.cs`
7. `Services/IThreadService.cs`
8. `Services/ThreadService.cs`
9. `Services/IImageService.cs`
10. `Services/ImageService.cs`
11. `Services/IThreadSummarizationService.cs`
12. `Services/ThreadSummarizationService.cs`
13. `Services/AnthropicChatClientAdapter.cs`

**Components:**
14. `Components/Pages/ClaudeV2.razor`
15. `Components/Pages/ClaudeV2.razor.cs`
16. `Components/Pages/TestDxChat.razor` (testing)

### MODIFY (5 files)

17. `duetGPT.csproj` (add NuGet packages)
18. `Program.cs` (register services, IChatClient)
19. `Components/Pages/Claude.razor.cs` (refactor to use services)
20. `Components/Layout/NavMenu.razor` (add /chat-v2 link)
21. `appsettings.json` (optional - connection string already correct)

### DELETE (6 files - after Claude.razor.cs refactor)

22. `Components/Pages/Claude.SendMessage.cs`
23. `Components/Pages/Claude.ThreadManagement.cs`
24. `Components/Pages/Claude.ImageHandling.cs`
25. `Components/Pages/Claude.Summarize.cs`
26. `Components/Pages/Claude.UpdateMethods.cs`
27. `Components/Pages/Claude.Helpers.cs`

---

## Critical Files

1. **`Components/Pages/Claude.SendMessage.cs`** (638 lines) - Core message logic to extract
2. **`Services/AnthropicService.cs`** - API wrapper to be wrapped by adapter
3. **`Program.cs`** - All service registrations
4. **`Data/ApplicationDbContext.cs`** - pgvector configuration for Docker validation
5. **`Components/Pages/Claude.razor.cs`** - Main component to refactor

---

## Risk Mitigation

**Risk 1: Database migration fails**
→ Backup verified, local PostgreSQL kept running, easy rollback

**Risk 2: Service extraction breaks functionality**
→ Incremental creation, test each service, keep old code until refactor complete

**Risk 3: Adapter doesn't support all features**
→ Build test component first, validate before full integration

**Risk 4: DxAIChat UI incompatible**
→ Parallel implementation, gradual migration, keep classic as fallback

**Risk 5: Performance degradation**
→ Load testing, profiling, optimize queries

---

## Success Criteria

- ✅ Docker container running healthy (COMPLETED - Phase 1)
- ✅ All data migrated successfully (COMPLETED - Phase 1)
- ⏳ 4 new services implemented and registered (Phase 2)
- ⏳ Claude.razor.cs < 300 lines (Phase 2)
- ⏳ IChatClient adapter functional (Phase 3)
- ⏳ ClaudeV2.razor working with all features (Phase 4)
- ⏳ No functionality regressions (To be verified)
- ⏳ Token/cost tracking accurate (To be verified)

---

## Implementation Order

**Day 1:** Phase 1 (Docker) + Phase 2 start
**Day 2:** Phase 2 finish + Phase 3 (adapter)
**Day 3:** Phase 4 (DxAIChat) + Testing
