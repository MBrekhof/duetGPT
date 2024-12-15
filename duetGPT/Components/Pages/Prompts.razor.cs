using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using duetGPT.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Markdig;

namespace duetGPT.Components.Pages;

/// <summary>
/// Code-behind class for the Prompts page component
/// </summary>
public partial class Prompts
{
  [Inject]
  private ApplicationDbContext Context { get; set; } = default!;

  [Inject]
  private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

  private List<Prompt> GridDataSource { get; set; } = new();
  private bool PopupVisible { get; set; }
  private string PopupTitle => CurrentPrompt?.PromptID == 0 ? "New Prompt" : "Edit Prompt";
  private Prompt CurrentPrompt { get; set; } = new();
  private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
      .Build();

  /// <summary>
  /// Initializes the component by loading the prompts data
  /// </summary>
  protected override async Task OnInitializedAsync()
  {
    await LoadData();
  }

  /// <summary>
  /// Loads all prompts from the database
  /// </summary>
  private async Task LoadData()
  {
    try
    {
      GridDataSource = await Context.Set<Prompt>().ToListAsync();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error fetching prompts: {ex.Message}");
    }
  }

  /// <summary>
  /// Shows the edit popup for creating or editing a prompt
  /// </summary>
  /// <param name="prompt">The prompt to edit, or null for creating a new prompt</param>
  private async Task ShowEditPopup(Prompt? prompt)
  {
    if (prompt == null)
    {
      CurrentPrompt = new Prompt();
    }
    else
    {
      // Fetch the prompt directly from the context to ensure it's tracked
      CurrentPrompt = await Context.Set<Prompt>().FindAsync(prompt.PromptID)
          ?? throw new InvalidOperationException($"Prompt with ID {prompt.PromptID} not found");
    }
    PopupVisible = true;
  }

  /// <summary>
  /// Saves the current prompt to the database
  /// </summary>
  private async Task SavePrompt()
  {
    try
    {
      if (CurrentPrompt.PromptID == 0)
      {
        Context.Add(CurrentPrompt);
      }
      // No need for explicit Update call since the entity is tracked
      await Context.SaveChangesAsync();
      await LoadData();
      PopupVisible = false;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error saving prompt: {ex.Message}");
    }
  }

  /// <summary>
  /// Deletes a prompt from the database
  /// </summary>
  /// <param name="promptId">The ID of the prompt to delete</param>
  private async Task DeletePrompt(int promptId)
  {
    try
    {
      var prompt = await Context.Set<Prompt>().FindAsync(promptId);
      if (prompt != null)
      {
        Context.Remove(prompt);
        await Context.SaveChangesAsync();
        await LoadData();
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error deleting prompt: {ex.Message}");
    }
  }

  /// <summary>
  /// Renders markdown content to HTML
  /// </summary>
  /// <param name="markdown">The markdown content to render</param>
  /// <returns>The rendered HTML content</returns>
  private string RenderMarkdown(string markdown)
  {
    var html = Markdown.ToHtml(markdown, Pipeline);
    return $"<div class=\"markdown-content\">{html}</div>";
  }
}