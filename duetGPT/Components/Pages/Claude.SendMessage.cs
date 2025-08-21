using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Anthropic.SDK.Common; // Added for ServerTools
using DevExpress.Blazor;
using duetGPT.Data;
using duetGPT.Services;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;


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

        //[Inject]
        //private ToolsService _serverTools { get; set; } = default!;

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
            { AnthropicModels.Claude35Haiku, new ModelCosts(0.00025m, 0.00025m) },
            { AnthropicModels.Claude4Sonnet, new ModelCosts(0.0003m, 0.0003m) },
            { AnthropicModels.Claude37Sonnet, new ModelCosts(0.00035m, 0.00035m) },
            { AnthropicModels.Claude4Opus, new ModelCosts(0.0004m, 0.0004m) }
        };

        /// <summary>
        /// Handles the send message click event and processes the user's message through the AI service
        /// </summary>
        async Task SendClick()
        {
            if (string.IsNullOrWhiteSpace(textInput))
            {
                return;
            }

            // Variables declared outside try for finally block access
            MessageResponse res = null;
            string markdown = string.Empty;
            string modelChosen = string.Empty;
            DuetMessage duetUserMessage = null;

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
                // Load existing messages from database if we have a thread and chatMessages is empty
                if (currentThread != null && !chatMessages.Any())
                {
                    await LoadMessagesFromDb();
                }
                modelChosen = GetModelChosen(ModelValue); // Initialize here
                _logger.LogInformation("Sending message using model: {Model}", modelChosen);

                await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Create and save DuetMessage for user input
                duetUserMessage = new DuetMessage // Initialize here
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

                string systemPrompt = @"You are an expert at analyzing user questions and providing accurate, relevant answers.
Use the following guidelines:
1. Prioritize information from the provided knowledge base when available
2. Supplement with your general knowledge when needed
3. Clearly indicate when you're using provided knowledge versus general knowledge
4. If the provided knowledge seems insufficient or irrelevant, rely on your general expertise
5. Ultrathink and Ultracheck your answer before answering";

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

                // Add web search instruction if enabled
                //if (EnableWebSearch)
                //{
                //    systemPrompt += "\n\nYou have access to web search capabilities. Use them when you need current information or when the provided knowledge base doesn't contain sufficient information to answer the user's question.";
                //}

                List<SystemMessage> systemMessages;

                try
                {
                    // Clear previous thinking content
                    ThinkingContent = string.Empty;

                    // Create message with image if available
                    var imageBytes = await GetCurrentImageBytes();
                    Message message; // This is the current user's input message
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
                                    new Anthropic.SDK.Messaging.TextContent
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

                    // Check if extended thinking is enabled and available for the current model
                    bool useStandardApi = false;
                    var client = _anthropicService.GetAnthropicClient();
                    if (EnableExtendedThinking && IsExtendedThinkingAvailable())
                    {
                        _logger.LogInformation("Enabling extended thinking for this request");

                        try
                        {
                            systemMessages = new List<SystemMessage>()
                            {
                                new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                            };
                            var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                                              forceUpdate: true, clearCache: true);
                            var extendedRequest = new MessageParameters()
                            {
                                Messages = chatMessages.Concat(new[] { message }).ToList(),
                                Model = AnthropicModels.Claude37Sonnet,
                                Stream = false,
                                MaxTokens = 20000,
                                Temperature = 1.0m,
                                System = systemMessages,
                                Thinking = new Anthropic.SDK.Messaging.ThinkingParameters()
                                {
                                    BudgetTokens = 16000
                                },
                                Tools = tools.ToList(),
                            };

                            if (EnableWebSearch)
                            {
                                if (extendedRequest.Tools == null) extendedRequest.Tools = new List<Anthropic.SDK.Common.Tool>();
                                var webSearchTool = ServerTools.GetWebSearchTool(5, null, new List<string>());
                                if (!extendedRequest.Tools.Any(t => t.GetType() == webSearchTool.GetType()))
                                {
                                    extendedRequest.Tools.Add(webSearchTool);
                                }
                                extendedRequest.ToolChoice = new ToolChoice() { Type = ToolChoiceType.Auto };
                            }

                            var extendedResponse = await client.Messages.GetClaudeMessageAsync(extendedRequest);
                            res = extendedResponse; // Assign directly

                            markdown = res.Message?.ToString() ?? "No message content in extended response.";

                            var thinkingContent = res.Message.ThinkingContent;
                            if (!string.IsNullOrEmpty(thinkingContent))
                            {
                                ThinkingContent = thinkingContent;
                                _logger.LogInformation("Received thinking content: {Length} characters", ThinkingContent.Length);
                            }
                            else
                            {
                                _logger.LogWarning("No thinking content was found in the response");
                                ThinkingContent = "Extended thinking was requested but not returned by the model. This may be due to API limitations or the specific query type.";
                            }

                            chatMessages.Add(message);
                            if (res.Message != null) chatMessages.Add(res.Message);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error using extended thinking feature. Falling back to standard API.");
                            useStandardApi = true;
                        }
                    }
                    else
                    {
                        useStandardApi = true;
                    }

                    if (useStandardApi)
                    {
                        _logger.LogInformation("Using standard API path.");
                        systemMessages = new List<SystemMessage>()
                            {
                                new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                            };

                        var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                                            forceUpdate: true, clearCache: true);

                        var apiCallMessages = new List<Message>(chatMessages);
                        apiCallMessages.Add(message);

                        var parameters = new MessageParameters
                        {
                            Messages = apiCallMessages,
                            Model = modelChosen,
                            MaxTokens = ModelValue == Model.Sonnet37 ? 8192 : 16384,
                            Stream = false,
                            Temperature = 1.0m,
                            System = systemMessages,
                            Tools = tools.ToList(),
                            ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto },
                        };

                        if (EnableWebSearch)
                        {
                            var webSearchTool = ServerTools.GetWebSearchTool(5, null, new List<string>());
                            if (!parameters.Tools.Any(t => t.GetType() == webSearchTool.GetType()))
                            {
                                parameters.Tools.Add(webSearchTool);
                                _logger.LogInformation("Web search tool added to tools list for this request");
                            }
                            else
                            {
                                _logger.LogInformation("Web search tool already present in tools list, not adding again");
                            }
                        }

                        if ((bool)parameters.Stream)
                        {
                            _logger.LogInformation("Using streaming API call.");
                            markdown = string.Empty; // Initialize markdown for streaming
                            var outputs = new List<MessageResponse>();
                            await foreach (var streamRes in client.Messages.StreamClaudeMessageAsync(parameters))
                            {
                                if (streamRes.Delta != null)
                                {
                                    markdown += streamRes.Delta.Text;
                                }
                                outputs.Add(streamRes);
                            }
                            res = outputs.LastOrDefault();
                            chatMessages.Add(message);
                            if (res?.Message != null) chatMessages.Add(res.Message);
                        }
                        else
                        {
                            _logger.LogInformation("Using non-streaming API call. Initial messages count: {Count}", parameters.Messages.Count);
                            res = await client.Messages.GetClaudeMessageAsync(parameters);
                            _logger.LogInformation("First API call completed. Stop Reason: {StopReason}", res.StopReason);

                            chatMessages.Add(message);
                            if (res.Message != null)
                            {
                                chatMessages.Add(res.Message);
                            }
                            else
                            {
                                _logger.LogWarning("First API response message was null.");
                            }

                            if (res.ToolCalls != null && res.ToolCalls.Any())
                            {
                                _logger.LogInformation("Tool calls received: {Count}", res.ToolCalls.Count);
                                foreach (var toolCall in res.ToolCalls)
                                {
                                    _logger.LogInformation("Invoking tool: {ToolName}, ID: {ToolId}", toolCall.Name, toolCall.Id);
                                    var toolResponseContent = await toolCall.InvokeAsync<string>();
                                    var toolMessage = new Message(toolCall, toolResponseContent);
                                    chatMessages.Add(toolMessage);
                                    _logger.LogInformation("Tool {ToolName} (ID: {ToolId}) invoked, response length: {Length}", toolCall.Name, toolCall.Id, toolResponseContent?.Length ?? 0);
                                }

                                var finalApiCallMessages = new List<Message>(chatMessages);

                                var finalParameters = new MessageParameters
                                {
                                    Messages = finalApiCallMessages,
                                    Model = parameters.Model,
                                    MaxTokens = parameters.MaxTokens,
                                    Stream = false,
                                    Temperature = parameters.Temperature,
                                    System = parameters.System,
                                    Tools = parameters.Tools,
                                    ToolChoice = parameters.ToolChoice
                                };

                                _logger.LogInformation("Making second API call with {Count} messages after tool results.", finalApiCallMessages.Count);
                                var finalResult = await client.Messages.GetClaudeMessageAsync(finalParameters);
                                _logger.LogInformation("Second API call completed. Stop Reason: {StopReason}", finalResult.StopReason);

                                markdown = finalResult.Message?.ToString() ?? "No message content in final response after tool use.";

                                if (finalResult.Message != null)
                                {
                                    chatMessages.Add(finalResult.Message);
                                }
                                else
                                {
                                    _logger.LogWarning("Final API response message was null after tool use.");
                                }
                                res = finalResult;
                            }
                            else
                            {
                                _logger.LogInformation("No tool calls in the first response.");
                                markdown = res.Message?.ToString() ?? "No message content in response.";
                            }
                        }
                    }

                    _logger.LogInformation("Successfully received response from AI service. Markdown preview: {MarkdownPreview}", markdown?.Substring(0, Math.Min(markdown.Length, 100)));
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

                if (res == null || res.Usage == null)
                {
                    _logger.LogError("API response or usage information is null. Cannot calculate tokens or cost accurately.");
                    Tokens = 0;
                }
                else
                {
                    Tokens = res.Usage.InputTokens + res.Usage.OutputTokens;
                    var costs = GetModelCosts(modelChosen);
                    decimal inputCost = CalculateCost(res.Usage.InputTokens, costs.InputRate);
                    decimal outputCost = CalculateCost(res.Usage.OutputTokens, costs.OutputRate);

                    if (duetUserMessage != null) // Ensure duetUserMessage is not null
                    {
                        duetUserMessage.TokenCount = res.Usage.InputTokens;
                        duetUserMessage.MessageCost = inputCost;
                        await dbContext.SaveChangesAsync();
                    }

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

                    UpdateTokensAsync(Tokens);
                    UpdateCostAsync(Cost + inputCost + outputCost);
                }

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

                textInput = "";
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
                if (ex.Message.Contains("Failed to get response from AI service"))
                {
                    // Already handled
                }
                else
                {
                    _logger.LogError(ex, "Error processing message in SendClick");
                    _toastService.ShowToast(new ToastOptions()
                    {
                        ProviderName = "ClaudePage",
                        ThemeMode = ToastThemeMode.Dark,
                        RenderStyle = ToastRenderStyle.Danger,
                        Title = "Processing Error",
                        Text = "An error occurred while processing your message. Please try again."
                    });
                }
            }
            finally
            {
                if (newThread && res != null && !string.IsNullOrEmpty(markdown) && currentThread != null && duetUserMessage != null && !string.IsNullOrEmpty(modelChosen))
                {
                    var client = _anthropicService.GetAnthropicClient();
                    await GenerateThreadTitle(client, modelChosen, duetUserMessage.Content, markdown);
                    newThread = false;
                }

                running = false;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Generates a descriptive title for the thread based on the conversation content
        /// </summary>
        /// <param name="client">The Anthropic client instance</param>
        /// <param name="modelChosen">The model to use for title generation</param>
        /// <param name="userMessage">The user's message</param>
        /// <param name="assistantResponse">The assistant's response</param>
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
                    },
                };

                var titleResponse = await client.Messages.GetClaudeMessageAsync(titleParameters);
                var generatedTitle = titleResponse.Message?.ToString()?.Trim('"', ' ', '\n') ?? "Chat Title";

                if (generatedTitle.Length > 100)
                {
                    generatedTitle = generatedTitle.Substring(0, 97) + "...";
                }

                if (currentThread != null)
                {
                    currentThread.Title = generatedTitle;
                    dbContext.Update(currentThread);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Thread title generated and saved: {Title}", generatedTitle);
                }
                else
                {
                    _logger.LogWarning("currentThread was null, cannot save generated title.");
                }

                if (titleResponse?.Usage != null)
                {
                    var costs = GetModelCosts(modelChosen);
                    decimal titleInputCost = CalculateCost(titleResponse.Usage.InputTokens, costs.InputRate);
                    decimal titleOutputCost = CalculateCost(titleResponse.Usage.OutputTokens, costs.OutputRate);

                    UpdateTokensAsync(Tokens + titleResponse.Usage.InputTokens + titleResponse.Usage.OutputTokens);
                    UpdateCostAsync(Cost + titleInputCost + titleOutputCost);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating thread title");
            }
        }

        /// <summary>
        /// Gets the model string based on the selected model enum value
        /// </summary>
        /// <param name="modelValue">The model enum value</param>
        /// <returns>The corresponding model string</returns>
        private string GetModelChosen(Model modelValue)
        {
            try
            {
                return modelValue switch
                {
                    Model.Haiku35 => AnthropicModels.Claude35Haiku,
                    Model.Sonnet4 => AnthropicModels.Claude4Sonnet,
                    Model.Sonnet37 => AnthropicModels.Claude37Sonnet,
                    Model.Opus4 => AnthropicModels.Claude4Opus,
                    _ => AnthropicModels.Claude4Sonnet
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting model chosen");
                return AnthropicModels.Claude4Sonnet;
            }
        }

        /// <summary>
        /// Gets the cost structure for a specific model
        /// </summary>
        /// <param name="model">The model string</param>
        /// <returns>The cost structure for the model</returns>
        private ModelCosts GetModelCosts(string model)
        {
            return MODEL_COSTS.TryGetValue(model, out var costs) ? costs : MODEL_COSTS[AnthropicModels.Claude4Sonnet];
        }

        /// <summary>
        /// Calculates the cost based on token count and rate per token
        /// </summary>
        /// <param name="tokens">Number of tokens</param>
        /// <param name="ratePerToken">Rate per token</param>
        /// <returns>The calculated cost</returns>
        private static decimal CalculateCost(int tokens, decimal ratePerToken)
        {
            try
            {
                return tokens * ratePerToken;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
