using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Markdig;
using duetGPT.Data;
using duetGPT.Services;
using Microsoft.EntityFrameworkCore;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        async Task SendClick()
        {
            try
            {
                var client = AnthropicService.GetAnthropicClient();
                string modelChosen = GetModelChosen(ModelValue);
                Logger.LogInformation("Sending message using model: {Model}", modelChosen);
                running = true;

                var userMessage = new Message(
                  RoleType.User, textInput, null
               );

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
                //await DbContext.SaveChangesAsync();
                DbContext.SaveChanges();

                userMessages.Add(userMessage);
                chatMessages = userMessages;
                AssociateDocumentsWithThread();
                var extrainfo = await GetThreadDocumentsContentAsync();

                if (extrainfo != null && extrainfo.Any())
                {
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

                    systemMessages = new List<SystemMessage>()
                    {
                        new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                    };
                    systemMessages.Add(new SystemMessage(string.Join("\n", extrainfo), new CacheControl() { Type = CacheControlType.ephemeral }));
                }

                var parameters = new MessageParameters()
                {
                    Messages = chatMessages,
                    Model = modelChosen,
                    MaxTokens = ModelValue == Model.Sonnet35 ? 8192 : 4096,
                    Stream = false,
                    Temperature = 1.0m,
                    System = systemMessages
                };

                string markdown = string.Empty;
                int totalTokens = 0;

                var res = await client.Messages.GetClaudeMessageAsync(parameters);
                userMessages.Add(res.Message);
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
                    Content = res.Content[0].ToString() ?? "No answer",
                    TokenCount = res.Usage.OutputTokens,
                    MessageCost = CalculateCost(res.Usage.OutputTokens, modelChosen)
                };
                DbContext.Add(duetAssistantMessage);
                DbContext.SaveChanges();

                // Generate thread title after first message exchange if not already set
                if (string.IsNullOrEmpty(currentThread.Title))
                {
                    await GenerateThreadTitle(client, modelChosen, textInput, res.Content[0].ToString());
                }

                // Update Tokens and Cost
                UpdateTokensAsync(Tokens + totalTokens);
                UpdateCostAsync(Cost + CalculateCost(totalTokens, modelChosen));

                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                markdown = res.Content[0].ToString() ?? "No answer";
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

                textInput = ""; // clear input.
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
