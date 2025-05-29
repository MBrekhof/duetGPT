using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
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
            { AnthropicModels.Claude3Haiku, new ModelCosts(0.00025m, 0.00025m) },
            { AnthropicModels.Claude3Sonnet, new ModelCosts(0.0003m, 0.0003m) },
            { AnthropicModels.Claude35Sonnet, new ModelCosts(0.00035m, 0.00035m) },
            { AnthropicModels.Claude37Sonnet, new ModelCosts(0.00035m, 0.00035m) },
            { AnthropicModels.Claude3Opus, new ModelCosts(0.0004m, 0.0004m) }
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

                // Web search will be handled by Anthropic's native web search tool when enabled
                // The search results will be automatically included in the AI response

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

                // Add web search instruction if enabled
                if (EnableWebSearch)
                {
                    systemPrompt += "\n\nYou have access to web search capabilities. Use them when you need current information or when the provided knowledge base doesn't contain sufficient information to answer the user's question.";
                }

                MessageResponse res = null;
                string markdown = string.Empty;

                try
                {
                    // Clear previous thinking content
                    ThinkingContent = string.Empty;

                    // Create message with image if available
                    var imageBytes = await GetCurrentImageBytes();
                    Message message;
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
                            // Create system messages for extended thinking
                            var systemMessages = new List<SystemMessage>()
              {
                  new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
              };
                            var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                                              forceUpdate: true, clearCache: true);
                            // Convert standard parameters to extended request
                            var extendedRequest = new MessageParameters()
                            {
                                Messages = chatMessages.Concat(new[] { message }).ToList(),
                                Model = AnthropicModels.Claude37Sonnet,
                                Stream = false,
                                MaxTokens = 20000,
                                Temperature = 1.0m,
                                System = systemMessages,  // Include system messages with knowledge content
                                Thinking = new Anthropic.SDK.Messaging.ThinkingParameters()
                                {
                                    BudgetTokens = 16000  // Allocate 16,000 tokens for thinking
                                },
                                Tools = tools.ToList(),
                            };

                            // Add web search tool if enabled
                            if (EnableWebSearch)
                            {
                                extendedRequest.Tools = new List<Anthropic.SDK.Common.Tool>
                {
                    ServerTools.GetWebSearchTool(5, null, new List<string>())
                };
                                extendedRequest.ToolChoice = new ToolChoice() { Type = ToolChoiceType.Auto };
                            }

                            // Call the custom API
                            var extendedResponse = await client.Messages.GetClaudeMessageAsync(extendedRequest);

                            // Extract the response
                            markdown = string.Join("\n", extendedResponse.Message.ToString());

                            //foreach (var toolCall in res.ToolCalls)
                            //{
                            //    var response = await toolCall.InvokeAsync<string>();

                            //    chatMessages.Add(new Message(toolCall, response));
                            //}

                            //var finalResult = await client.Messages.GetClaudeMessageAsync(extendedRequest);
                            //markdown = string.Join("\n", finalResult.Message.ToString());

                            // Extract thinking content using the helper method
                            var thinkingContent = extendedResponse.Message.ThinkingContent;
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

                            // Create a compatible response object for the rest of the code
                            res = new MessageResponse
                            {
                                Content = extendedResponse.Content,
                                Usage = new Anthropic.SDK.Messaging.Usage { InputTokens = extendedResponse.Usage.InputTokens, OutputTokens = extendedResponse.Usage.OutputTokens }
                            };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error using extended thinking feature. Falling back to standard API.");
                            // Fall back to standard API
                            useStandardApi = true;
                        }
                    }
                    else
                    {
                        useStandardApi = true;
                    }

                    if (useStandardApi)
                    {
                        // Handle Anthropic models with standard API

                        systemMessages = new List<SystemMessage>()
                            {
                                new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                            };

                        var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                                            forceUpdate: true, clearCache: true);

                        // Add user message to chat history before API call
                        chatMessages.Add(message);
                        // Include full chat history in API call
                        var parameters = new MessageParameters
                        {
                            Messages = chatMessages.Concat(new[] { message }).ToList(),
                            Model = modelChosen,
                            MaxTokens = ModelValue == Model.Sonnet37 ? 8192 : 16384,
                            Stream = false,// !hasImage, // Don't stream if we have an image
                            Temperature = 1.0m,
                            System = systemMessages,
                            Tools = tools.ToList(),
                            ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto },
                        };

                        // Add web search tool if enabled and not already present
                        if (EnableWebSearch)
                        {
                            var webSearchTool = ServerTools.GetWebSearchTool(5, null, new List<string>());
                            // Only add if not already present (by type or unique property)
                            if (!parameters.Tools.Any(t => t.GetType() == webSearchTool.GetType()))
                            {
                                parameters.Tools.Add(webSearchTool);
                                _logger.LogInformation("Web search tool added to tools list for this request");
                            }
                            else
                            {
                                _logger.LogInformation("Web search tool already present in tools list, not adding again");
                            }
                            //parameters.ToolChoice = new ToolChoice() { Type = ToolChoiceType.Auto };
                        }

                        // Add user message to chat history

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
                            chatMessages.Add(res.Message);
                            
                            foreach (var toolCall in res.ToolCalls)
                            {
                                var response = await toolCall.InvokeAsync<string>();
                                var answer = new Message(toolCall, response);

                                chatMessages.Add(answer);
                            }

                            var finalResult = await client.Messages.GetClaudeMessageAsync(parameters);
                            markdown = finalResult.Content[0].ToString() ?? "No answer";
                        }
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
                // Assistant response already added to chat history in the API call section

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
                //var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                //                                       forceUpdate: true, clearCache: true);
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
                    //Tools = tools.ToList(),
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
                    _ => AnthropicModels.Claude35Sonnet
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting model chosen");
                return AnthropicModels.Claude35Sonnet; // Default fallback
            }
        }

        /// <summary>
        /// Gets the cost structure for a specific model
        /// </summary>
        /// <param name="model">The model string</param>
        /// <returns>The cost structure for the model</returns>
        private ModelCosts GetModelCosts(string model)
        {
            return MODEL_COSTS.TryGetValue(model, out var costs) ? costs : MODEL_COSTS[AnthropicModels.Claude35Sonnet];
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
                return 0; // Return 0 if calculation fails
            }
        }
    }
}
