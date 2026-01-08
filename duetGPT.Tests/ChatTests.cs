using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.RegularExpressions;

namespace duetGPT.Tests;

/// <summary>
/// Tests for the chat interface functionality
/// </summary>
public class ChatTests : PlaywrightTestBase
{
    [Test]
    public async Task CanLoadChatPage()
    {
        // Navigate to chat page (authenticated via storage state)
        await Page.GotoAsync($"{BaseUrl}/chat-v2");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify we're on the chat page (not redirected to login)
        Assert.That(Page.Url, Does.Not.Contain("/Account/Login"), "Should not be redirected to login page");

        // Verify DxAIChat component loaded (it has custom-ai-chat class)
        var chatComponent = Page.Locator(".custom-ai-chat");
        await Expect(chatComponent).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Verify header text is visible
        var headerText = Page.Locator(".title");
        await Expect(headerText).ToBeVisibleAsync();
    }

    [Test]
    public async Task CanSendMessage()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for chat component to load
        await Page.WaitForSelectorAsync(".custom-ai-chat", new() { Timeout = 10000 });

        // DevExpress DxAIChat uses specific structure - look for textarea in the chat
        var messageInput = Page.Locator(".custom-ai-chat textarea").First;
        await messageInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Type message
        await messageInput.FillAsync("Hello, test message");

        // Verify text was entered
        var value = await messageInput.InputValueAsync();
        Assert.That(value, Is.EqualTo("Hello, test message"), "Message should be typed in input");

        // Send message by pressing Enter (alternative to clicking button)
        await messageInput.PressAsync("Enter");

        // Wait for message to appear (DxAIChat renders messages in specific structure)
        // Give more time for the message to be processed and displayed
        var chatMessages = Page.Locator(".custom-ai-chat [role='article'], .custom-ai-chat .dxbl-aichat-message");
        await Expect(chatMessages.First).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Test]
    public async Task NewThreadButtonClearsChat()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.WaitForSelectorAsync(".custom-ai-chat", new() { Timeout = 10000 });

        // Click New Thread button (has icon and text "New Thread")
        var newThreadButton = Page.GetByRole(AriaRole.Button, new() { NameString = "New Thread" });
        await newThreadButton.ClickAsync();

        // Confirm in DxPopup dialog (button says "Confirm")
        var confirmButton = Page.GetByRole(AriaRole.Button, new() { NameString = "Confirm" });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 });
        await confirmButton.ClickAsync();

        // Wait for UI to update
        await Page.WaitForTimeoutAsync(1000);

        // Verify chat is cleared - check that there are no messages in the chat
        var chatMessages = Page.Locator(".custom-ai-chat [role='article'], .custom-ai-chat .dxbl-aichat-message");
        var count = await chatMessages.CountAsync();
        Assert.That(count, Is.EqualTo(0), "Chat should be empty after new thread");
    }

    [Test]
    public async Task RAGToggleVisible()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify RAG toggle exists - look for the container with "Enable RAG" text and nearby checkbox
        var ragText = Page.GetByText("Enable RAG");
        await Expect(ragText).ToBeVisibleAsync(new() { Timeout = 5000 });

        // DevExpress DxCheckBox is in a container with the text, find input[type='checkbox'] near the text
        var ragContainer = Page.Locator(".toggle-container").Filter(new() { HasText = "Enable RAG" });
        await Expect(ragContainer).ToBeVisibleAsync();
    }

    [Test]
    public async Task ModelSelectionWorks()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // DevExpress DxComboBox renders as input with role=combobox in header area
        // Look for combobox in the combobox-container div (first one is model selector)
        var modelSelector = Page.Locator(".combobox-container [role='combobox']").First;
        await Expect(modelSelector).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click to open dropdown to verify it works
        await modelSelector.ClickAsync();

        // Wait a moment for dropdown to open
        await Page.WaitForTimeoutAsync(1000);

        // DevExpress shows options in a dropdown dialog when opened
        // Use more specific selector for the dropdown that appears after clicking
        var dropdown = Page.Locator("dxbl-dropdown-dialog[role='listbox']").First;

        // Check if dropdown appeared
        await Expect(dropdown).ToBeVisibleAsync(new() { Timeout = 3000 });
    }
}
