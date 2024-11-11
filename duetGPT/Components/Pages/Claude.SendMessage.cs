using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Markdig;
using duetGPT.Data;

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
                await DbContext.SaveChangesAsync();

                userMessages.Add(userMessage);
                chatMessages = userMessages;
                await AssociateDocumentsWithThread();
                var extrainfo = await GetThreadDocumentsContentAsync();

                if (extrainfo != null && extrainfo.Any())
                {
                    systemMessages = new List<SystemMessage>()
                {
                    new SystemMessage("You are an expert at analyzing an user question and what they really want to know. If necessary and possible use your general knowledge also",
                        new CacheControl() { Type = CacheControlType.ephemeral })
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
                await DbContext.SaveChangesAsync();

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
                await DbContext.SaveChangesAsync();

                // Update Tokens and Cost
                await UpdateTokensAsync(Tokens + totalTokens);
                await UpdateCostAsync(Cost + CalculateCost(totalTokens, modelChosen));

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
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing message");
            }
            finally
            {
                running = false;
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
