using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using DevExpress.Blazor;
using duetGPT.Data;
using duetGPT.Services;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tavily;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        [Inject]
        private OpenAIService _openAIService { get; set; } = default!;

        [Inject]
        private AnthropicService _anthropicService { get; set; } = default!;

        [Inject]
        private IKnowledgeService _knowledgeService { get; set; } = default!;

        [Inject]
        private IConfiguration _configuration { get; set; } = default!;

        [Inject]
        private ILogger<Claude> _logger { get; set; } = default!;

        [Inject]
        private IDbContextFactory<ApplicationDbContext> _dbContextFactory { get; set; } = default!;

        [Inject]
        private IToastNotificationService _toastService { get; set; } = default!;

        private record struct ModelCosts(decimal InputRate, decimal OutputRate);

        private static readonly Dictionary<string, ModelCosts> MODEL_COSTS = new()
        {
            { AnthropicModels.Claude3Haiku, new ModelCosts(0.00025m, 0.00025m) },
            { AnthropicModels.Claude3Sonnet, new ModelCosts(0.0003m, 0.0003m) },
            { AnthropicModels.Claude35Sonnet, new ModelCosts(0.00035m, 0.00035m) },
            { "claude-3-7-sonnet-20250219", new ModelCosts(0.00035m, 0.00035m) },
            { AnthropicModels.Claude3Opus, new ModelCosts(0.0004m, 0.0004m) }
        };

        async Task SendClick()
        {
            if (string.IsNullOrWhiteSpace(textInput))
            {
                return;
            }
            try
            {
                running = true;
                // Force immediate UI refresh
                await InvokeAsync(StateHasChanged);

                // Create thread if it doesn't exist yet
                if (currentThread == null)
                {
                    currentThread = await CreateNewThread();
                    newThread = true; // Set flag to generate title after first message exchange
                }
                // If thread exists but has default title, mark it for title generation
                else if (string.IsNullOrEmpty(currentThread.Title) || currentThread.Title == "Not yet created")
                {
                    newThread = true; // Ensure title gets generated after this message exchange
                }
                string modelChosen = GetModelChosen(ModelValue);
                _logger.LogInformation("Sending message using model: {Model}", modelChosen);

                await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Create and save DuetMessage for user input
                var duetUserMessage = new DuetMessage
                {
                    ThreadId = currentThread.Id,
                    Role = "user",
                    Content = textInput,
                    TokenCount = 0, // Will be updated when we get response
                    MessageCost = 0 // Will be updated when we get response
                };
                dbContext.Add(duetUserMessage);
                await dbContext.SaveChangesAsync();

                // Get relevant knowledge from vector database
                List<string> knowledgeContent = new List<string>();
                try
                {
                    var relevantKnowledge = await _knowledgeService.GetRelevantKnowledgeAsync(textInput);
                    if (relevantKnowledge != null && relevantKnowledge.Any())
                    {
                        knowledgeContent.AddRange(relevantKnowledge.Select(k => k.Content));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving relevant knowledge");
                }

                // Add document content if files are selected
                if (SelectedFiles != null && SelectedFiles.Any())
                {
                    await AssociateDocumentsWithThread();
                    var threadDocs = await GetThreadDocumentsContentAsync();
                    if (threadDocs != null && threadDocs.Any())
                    {
                        knowledgeContent.AddRange(threadDocs);
                    }
                }

                // Perform web search if enabled
                if (EnableWebSearch)
                {
                    try
                    {
                        var tavilyApiKey = _configuration["Tavily:ApiKey"];
                        if (!string.IsNullOrEmpty(tavilyApiKey))
                        {
                            using var tavilyClient = new TavilyClient();
                            var searchResponse = await tavilyClient.SearchAsync(
                                apiKey: tavilyApiKey,
                                query: textInput);

                            if (searchResponse?.Results != null)
                            {
                                var webResults = searchResponse.Results
                                    .OrderByDescending(r => r.Score)
                                    .Take(3) // Limit to top 3 most relevant results
                                    .Select(r => $"Source: {r.Url}\nTitle: {r.Title}\nContent: {r.Content}");

                                knowledgeContent.Add("\nWeb Search Results:\n" + string.Join("\n---\n", webResults));
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Tavily API key not found in configuration");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error performing web search");
                    }
                }

                string systemPrompt = @"You are an expert at analyzing user questions and providing accurate, relevant answers.
Use the following guidelines:
1. Prioritize information from the provided knowledge base when available
2. Supplement with your general knowledge when needed
3. Clearly indicate when you're using provided knowledge versus general knowledge
4. If the provided knowledge seems insufficient or irrelevant, rely on your general expertise";

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

                // Update system messages with knowledge and document content if available
                if (knowledgeContent.Any())
                {
                    systemPrompt += "\n\nRelevant knowledge base content:\n" + string.Join("\n---\n", knowledgeContent);
                }

                MessageResponse res;
                string markdown;

                try
                {

                    // Handle Anthropic models
                    var client = _anthropicService.GetAnthropicClient();
                    systemMessages = new List<SystemMessage>()
                        {
                            new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                        };

                    Message message;

                    // Create message with image if available
                    var imageBytes = await GetCurrentImageBytes();
                    if (imageBytes != null && CurrentImageType != null)
                    {
                        string base64Data = Convert.ToBase64String(imageBytes);

                        message = new Message
                        {
                            Role = RoleType.User,
                            Content = new List<ContentBase>
                                {
                                    new ImageContent
                                    {
                                        Source = new ImageSource
                                        {
                                            MediaType = CurrentImageType,
                                            Data = base64Data
                                        }
                                    },
                                    new TextContent
                                    {
                                        Text = textInput
                                    }
                                }
                        };
                    }
                    else
                    {
                        message = new Message(RoleType.User, textInput, null);
                    }

                    // Explicitly check for image content
                    bool hasImage = false;
                    if (message.Content != null)
                    {
                        hasImage = message.Content.Any(c => c is ImageContent);
                    }

                    // Include full chat history in API call
                    var parameters = new MessageParameters
                    {
                        Messages = chatMessages.Concat(new[] { message }).ToList(),
                        Model = modelChosen,
                        MaxTokens = ModelValue == Model.Sonnet35 ? 8192 : 4096,
                        Stream = !hasImage, // Don't stream if we have an image
                        Temperature = 1.0m,
                        System = systemMessages
                    };

                    // Add user message to chat history
                    chatMessages.Add(message);

                    if ((bool)parameters.Stream)
                    {
                        markdown = string.Empty;
                        var outputs = new List<MessageResponse>();
                        await foreach (var streamRes in client.Messages.StreamClaudeMessageAsync(parameters))
                        {
                            if (streamRes.Delta != null)
                            {
                                markdown += streamRes.Delta.Text;
                            }
                            outputs.Add(streamRes);
                        }
                        res = outputs.Last();
                    }
                    else
                    {
                        res = await client.Messages.GetClaudeMessageAsync(parameters);
                        markdown = res.Content[0].ToString() ?? "No answer";
                    }


                    _logger.LogInformation("Successfully received response from AI service");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get response from AI service");
                    _toastService.ShowToast(new ToastOptions()
                    {
                        ProviderName = "ClaudePage",
                        ThemeMode = ToastThemeMode.Dark,
                        RenderStyle = ToastRenderStyle.Danger,
                        Title = "API Error",
                        Text = "Failed to get response from AI service. Please try again."
                    });
                    throw;
                }

                Tokens = res.Usage.InputTokens + res.Usage.OutputTokens;

                // Calculate costs using separate rates for input and output tokens
                var costs = GetModelCosts(modelChosen);
                decimal inputCost = CalculateCost(res.Usage.InputTokens, costs.InputRate);
                decimal outputCost = CalculateCost(res.Usage.OutputTokens, costs.OutputRate);

                // Update user message with token count and cost
                duetUserMessage.TokenCount = res.Usage.InputTokens;
                duetUserMessage.MessageCost = inputCost;
                await dbContext.SaveChangesAsync();

                // Create and save assistant message
                var duetAssistantMessage = new DuetMessage
                {
                    ThreadId = currentThread.Id,
                    Role = "assistant",
                    Content = markdown,
                    TokenCount = res.Usage.OutputTokens,
                    MessageCost = outputCost
                };
                dbContext.Add(duetAssistantMessage);
                await dbContext.SaveChangesAsync();

                // Add assistant response to chat history
                var assistantMessage = new Message(RoleType.Assistant, markdown, null);
                chatMessages.Add(assistantMessage);

                // Generate thread title if this is a new thread or needs a title update
                if (newThread)
                {
                    var client = _anthropicService.GetAnthropicClient();
                    await GenerateThreadTitle(client, modelChosen, textInput, markdown);
                    newThread = false; // Reset the flag after generating the title
                }

                // Update Tokens and Cost
                UpdateTokensAsync(Tokens);
                UpdateCostAsync(Cost + inputCost + outputCost);

                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                if (textInput != null)
                    formattedMessages.Add(Markdown.ToHtml(textInput, pipeline));

                if (!string.IsNullOrEmpty(markdown))
                {
                    formattedMessages.Add(Markdown.ToHtml(markdown, pipeline));
                }
                else
                {
                    formattedMessages.Add(Markdown.ToHtml("Sorry, no response..", pipeline));
                }

                textInput = ""; // clear input
                _logger.LogInformation("Message sent and processed successfully");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error while communicating with AI service");
                _toastService.ShowToast(new ToastOptions()
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
                _logger.LogError(ex, "Error processing message");
                _toastService.ShowToast(new ToastOptions()
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

        private async Task GenerateThreadTitle(AnthropicClient client, string modelChosen, string userMessage, string assistantResponse)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            try
            {
                var titlePrompt = new Message(
                    RoleType.User,
                    $"Based on this conversation, generate a concise and descriptive title (maximum 100 characters):\n\nUser: {userMessage}\n\nAssistant: {assistantResponse}",
                    null
                );

                var titleParameters = new MessageParameters()
                {
                    Messages = new List<Message> { titlePrompt },
                    Model = modelChosen,
                    MaxTokens = 100,
                    Stream = false,
                    Temperature = 0.7m,
                    System = new List<SystemMessage>
                    {
                        new SystemMessage(
                            "Generate a concise, descriptive title that captures the main topic or purpose of the conversation. The title should be clear and informative, but not exceed 100 characters.",
                            new CacheControl() { Type = CacheControlType.ephemeral }
                        )
                    }
                };

                var titleResponse = await client.Messages.GetClaudeMessageAsync(titleParameters);
                var generatedTitle = titleResponse.Content[0].ToString().Trim('"', ' ', '\n');

                // Ensure title doesn't exceed 100 characters
                if (generatedTitle.Length > 100)
                {
                    generatedTitle = generatedTitle.Substring(0, 97) + "...";
                }

                // Update thread title
                currentThread.Title = generatedTitle;
                dbContext.Update(currentThread);
                await dbContext.SaveChangesAsync();

                // Calculate title generation costs
                var costs = GetModelCosts(modelChosen);
                decimal titleInputCost = CalculateCost(titleResponse.Usage.InputTokens, costs.InputRate);
                decimal titleOutputCost = CalculateCost(titleResponse.Usage.OutputTokens, costs.OutputRate);

                // Update tokens and cost
                UpdateTokensAsync(Tokens + titleResponse.Usage.InputTokens + titleResponse.Usage.OutputTokens);
                UpdateCostAsync(Cost + titleInputCost + titleOutputCost);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating thread title");
                // Don't throw - we don't want to interrupt the main conversation flow if title generation fails
            }
        }

        private string GetModelChosen(Model modelValue)
        {
            try
            {
                return modelValue switch
                {
                    Model.Haiku35 => "claude-3-5-haiku-20241022",
                    Model.Sonnet => AnthropicModels.Claude3Sonnet,
                    Model.Sonnet35 => AnthropicModels.Claude35Sonnet,
                    Model.Sonnet37 => "claude-3-7-sonnet-20250219",
                    Model.Opus => AnthropicModels.Claude3Opus,

                    _ => throw new ArgumentOutOfRangeException(nameof(modelValue),
                        $"Not expected model value: {modelValue}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting model choice for value: {ModelValue}", modelValue);
                throw;
            }
        }

        private ModelCosts GetModelCosts(string model)
        {
            if (MODEL_COSTS.TryGetValue(model, out var costs))
            {
                return costs;
            }

            // Log warning for unrecognized model and use default rates
            _logger.LogWarning("Unrecognized model: {Model}. Using default rates.", model);
            return new ModelCosts(0.0003m, 0.0003m);
        }

        private static decimal CalculateCost(int tokens, decimal ratePerToken)
        {
            if (tokens < 0)
            {
                throw new ArgumentException("Token count cannot be negative", nameof(tokens));
            }

            try
            {
                return tokens * ratePerToken / 1000m; // Cost per 1000 tokens
            }
            catch (OverflowException ex)
            {
                throw new OverflowException("Cost calculation resulted in overflow. Please check token count and rates.", ex);
            }
        }
    }
}
