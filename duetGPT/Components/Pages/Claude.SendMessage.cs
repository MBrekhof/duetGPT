using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
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
        private OpenAIService OpenAIService { get; set; } = default!;

        [Inject]
        private IKnowledgeService KnowledgeService { get; set; } = default!;

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
                    await CreateNewThread();
                    newThread = true;
                }

                var client = AnthropicService.GetAnthropicClient();
                string modelChosen = GetModelChosen(ModelValue);
                Logger.LogInformation("Sending message using model: {Model}", modelChosen);

                // Create and save DuetMessage for user input
                var duetUserMessage = new DuetMessage
                {
                    ThreadId = currentThread.Id,
                    Role = "user",
                    Content = textInput,
                    TokenCount = 0, // Will be updated when we get response
                    MessageCost = 0 // Will be updated when we get response
                };
                DbContext.Add(duetUserMessage);
                DbContext.SaveChanges();

                // Only associate and get documents if files are selected
                List<string> knowledgeContent = new List<string>();
                if (SelectedFiles != null && SelectedFiles.Any())
                {
                    AssociateDocumentsWithThread();
                    var threadDocs = await GetThreadDocumentsContentAsync();
                    if (threadDocs != null && threadDocs.Any())
                    {
                        knowledgeContent = threadDocs;
                    }
                }

                string systemPrompt = "You are an expert at analyzing an user question and what they really want to know. If necessary and possible use your general knowledge also";

                // Get selected prompt content if available
                if (!string.IsNullOrEmpty(SelectedPrompt))
                {
                    var selectedPromptContent = await DbContext.Set<Prompt>()
                        .Where(p => p.Name == SelectedPrompt)
                        .Select(p => p.Content)
                        .FirstOrDefaultAsync();

                    if (!string.IsNullOrEmpty(selectedPromptContent))
                    {
                        systemPrompt = selectedPromptContent;
                    }
                }

                if (knowledgeContent.Any())
                {
                    systemPrompt += "\n\n" + string.Join("\n", knowledgeContent);
                }

                MessageResponse res;
                string markdown;

                try
                {
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

                    var parameters = new MessageParameters
                    {
                        Messages = new List<Message> { message },
                        Model = modelChosen,
                        MaxTokens = ModelValue == Model.Sonnet35 ? 8192 : 4096,
                        Stream = !hasImage, // Don't stream if we have an image
                        Temperature = 1.0m,
                        System = new List<SystemMessage>
                        {
                            new SystemMessage(systemPrompt, new CacheControl { Type = CacheControlType.ephemeral })
                        }
                    };

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

                    Logger.LogInformation("Successfully received response from Claude API");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to get response from Claude API");
                    ErrorService.ShowError("Failed to get response from AI service. Please try again.");
                    throw;
                }

                Tokens = res.Usage.InputTokens + res.Usage.OutputTokens;

                // Update user message with token count and cost
                duetUserMessage.TokenCount = res.Usage.InputTokens;
                duetUserMessage.MessageCost = CalculateCost(res.Usage.InputTokens, modelChosen);
                DbContext.SaveChanges();

                // Create and save DuetMessage for assistant response
                var duetAssistantMessage = new DuetMessage
                {
                    ThreadId = currentThread.Id,
                    Role = "assistant",
                    Content = markdown,
                    TokenCount = res.Usage.OutputTokens,
                    MessageCost = CalculateCost(res.Usage.OutputTokens, modelChosen)
                };
                DbContext.Add(duetAssistantMessage);
                DbContext.SaveChanges();

                // Generate thread title after first message exchange if not already set
                if (newThread)
                {
                    await GenerateThreadTitle(client, modelChosen, textInput, markdown);
                    newThread = false;
                }

                // Update Tokens and Cost
                UpdateTokensAsync(Tokens);
                UpdateCostAsync(Cost + CalculateCost(Tokens, modelChosen));

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
                ImageUrl = null; // clear image URL for display
                await ClearImageData(); // clear image data and temp file
                Logger.LogInformation("Message sent and processed successfully");
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Network error while communicating with Anthropic API");
                ErrorService.ShowError("Error communicating with AI service. Please check your network connection and try again.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing message");
                ErrorService.ShowError("An error occurred while processing your message. Please try again.");
            }
            finally
            {
                running = false;
                StateHasChanged();
            }
        }

        private async Task GenerateThreadTitle(AnthropicClient client, string modelChosen, string userMessage, string assistantResponse)
        {
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
                DbContext.Update(currentThread);
                DbContext.SaveChanges();

                // Update tokens and cost
                UpdateTokensAsync(Tokens + titleResponse.Usage.InputTokens + titleResponse.Usage.OutputTokens);
                UpdateCostAsync(Cost + CalculateCost(titleResponse.Usage.InputTokens + titleResponse.Usage.OutputTokens, modelChosen));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error generating thread title");
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
                    Model.Opus => AnthropicModels.Claude3Opus,
                    _ => throw new ArgumentOutOfRangeException(nameof(modelValue),
                        $"Not expected model value: {modelValue}")
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error getting model choice for value: {ModelValue}", modelValue);
                throw;
            }
        }

        private decimal CalculateCost(int tokens, string model)
        {
            try
            {
                decimal rate = model switch
                {
                    AnthropicModels.Claude3Haiku => 0.00025m,
                    AnthropicModels.Claude3Sonnet => 0.0003m,
                    AnthropicModels.Claude35Sonnet => 0.00035m,
                    AnthropicModels.Claude3Opus => 0.0004m,
                    _ => 0.0003m // Default rate
                };

                return tokens * rate / 1000; // Cost per 1000 tokens
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calculating cost for tokens: {Tokens}, model: {Model}", tokens, model);
                throw;
            }
        }
    }
}
