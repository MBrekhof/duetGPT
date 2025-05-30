using Anthropic.SDK.Messaging;
using duetGPT.Data;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Claims;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        private async Task<DuetThread> CreateNewThread()
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            try
            {
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var user = authState.User;
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (userId == null)
                {
                    Logger.LogWarning("Attempted to create a new thread without a valid user ID");
                    throw new InvalidOperationException("User ID is required to create a new thread");
                }

                string systemPrompt = "You are an expert at analyzing an user question and what they really want to know. If necessary and possible use your general knowledge also";

                // Get selected prompt content if available
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

                // Initialize system messages with the selected prompt
                systemMessages = new List<SystemMessage>()
                {
                    new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                };

                // Initialize empty message lists
                chatMessages = new List<Message>();
                formattedMessages = new List<string>();

                Logger.LogInformation("Creating new thread for user {UserId}", userId);
                var thread = new DuetThread
                {
                    UserId = userId,
                    StartTime = DateTime.UtcNow,
                    TotalTokens = 0,
                    Cost = 0,
                    Title = "Not yet created" // Set initial title
                };

                // Save the thread immediately
                dbContext.Threads.Add(thread);
                await dbContext.SaveChangesAsync();

                Logger.LogInformation("New thread created with ID {ThreadId}", thread.Id);
                return thread;
            }
            catch (DbUpdateException ex)
            {
                Logger.LogError(ex, "Database error while creating new thread");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error creating new thread");
                throw;
            }
        }

        private async Task ClearThread()
        {
            try
            {
                Logger.LogInformation("Clearing thread");
                chatMessages.Clear();
                formattedMessages.Clear();
                UpdateTokensAsync(0);
                UpdateCostAsync(0);
                SelectedFiles = Enumerable.Empty<int>(); // Clear selected files

                // Create and save new thread
                currentThread = await CreateNewThread(); // CreateNewThread now handles database save
                newThread = true; // Mark as new thread to ensure title generation after first message
                Logger.LogInformation("Thread cleared and new thread created with ID {ThreadId}", currentThread.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error clearing thread");
                throw;
            }
        }

        private async Task AssociateDocumentsWithThread()
        {
            if (currentThread == null || !SelectedFiles.Any()) return;

            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            try
            {
                Logger.LogInformation("Associating documents with thread {ThreadId}", currentThread.Id);
                var selectedDocuments = await dbContext.Documents
                    .Where(d => SelectedFiles.Contains(d.Id))
                    .ToListAsync();

                var thread = await dbContext.Threads
                    .Include(t => t.ThreadDocuments)
                    .FirstOrDefaultAsync(t => t.Id == currentThread.Id);

                if (thread == null)
                {
                    Logger.LogError("Thread not found in database");
                    return;
                }

                if (thread.ThreadDocuments == null)
                {
                    thread.ThreadDocuments = new List<ThreadDocument>();
                }

                var newThreadDocuments = selectedDocuments
                    .Select(document => new ThreadDocument
                    {
                        ThreadId = thread.Id,
                        DocumentId = document.Id
                    })
                    .ToList();

                thread.ThreadDocuments.AddRange(newThreadDocuments);
                await dbContext.SaveChangesAsync();

                // Update the current thread with the new documents
                currentThread = thread;

                Logger.LogInformation("Associated {Count} documents with thread {ThreadId}", selectedDocuments.Count, thread.Id);
            }
            catch (DbUpdateException ex)
            {
                Logger.LogError(ex, "Database error while associating documents with thread");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error associating documents with thread");
                throw;
            }
        }

        private async Task<List<string>> GetThreadDocumentsContentAsync()
        {
            if (currentThread == null || currentThread.ThreadDocuments == null || !currentThread.ThreadDocuments.Any())
            {
                Logger.LogInformation("No documents associated with the current thread.");
                return new List<string>();
            }

            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            var documentIds = currentThread.ThreadDocuments.Select(td => td.DocumentId).ToList();
            var documents = await dbContext.Documents
                .Where(d => documentIds.Contains(d.Id))
                .ToListAsync();

            var documentContents = new List<string>();

            foreach (var document in documents)
            {
                string plainText = string.Empty;

                if (document.ContentType == "application/pdf")
                {
                    plainText = ExtractTextFromPdf(document.Content);
                }
                else if (document.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    plainText = ExtractTextFromDocx(document.Content);
                }
                else if (document.ContentType == "application/msword")
                {
                    plainText = ExtractTextFromDoc(document.Content);
                }
                else if (document.ContentType == "text/plain" || document.ContentType == "application/octet-stream" ||
                     document.ContentType == "text/json" || document.ContentType == "text/xml")
                {
                    plainText = System.Text.Encoding.UTF8.GetString(document.Content);
                }
                documentContents.Add("Documentname: " + document.FileName + " " + plainText);
            }

            Logger.LogInformation("Retrieved and converted content for {Count} documents associated with the current thread.", documentContents.Count);

            return documentContents;
        }
    }
}
