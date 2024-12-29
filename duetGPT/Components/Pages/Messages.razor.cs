using Microsoft.EntityFrameworkCore;
using DevExpress.Blazor;
using duetGPT.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Markdig;

namespace duetGPT.Components.Pages;

public partial class Messages
{
  [Inject]
  private ApplicationDbContext Context { get; set; }

  [Inject]
  private AuthenticationStateProvider AuthenticationStateProvider { get; set; }

  private List<DuetThread> GridDataSource { get; set; }
  private List<DuetMessage> ThreadMessages { get; set; }
  private string ErrorMessage { get; set; }
  private bool PopupVisible { get; set; }
  private bool DeleteConfirmationVisible { get; set; }
  private DuetThread ThreadToDelete { get; set; }

  /// <summary>
  /// Initializes the component by loading the user's message threads
  /// </summary>
  protected override async Task OnInitializedAsync()
  {
    try
    {
      var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
      var user = authState.User;
      var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

      if (string.IsNullOrEmpty(userId))
      {
        ErrorMessage = "User ID not found";
        return;
      }

      GridDataSource = await Context.Threads
          .Where(t => t.UserId == userId)
          .OrderByDescending(t => t.StartTime)
          .AsNoTracking()
          .ToListAsync();

      foreach (var thread in GridDataSource)
      {
        thread.StartTime = thread.StartTime.ToLocalTime();
      }
    }
    catch (Exception ex)
    {
      ErrorMessage = $"Error loading threads: {ex.Message}";
    }
  }

  /// <summary>
  /// Loads and displays messages for a specific thread
  /// </summary>
  /// <param name="thread">The thread to show messages for</param>
  private async Task ShowThreadMessages(DuetThread thread)
  {
    try
    {
      ThreadMessages = await Context.Messages
          .Where(m => m.ThreadId == thread.Id)
          .OrderByDescending(m => m.Id)
          .AsNoTracking()
          .ToListAsync();

      foreach (var message in ThreadMessages)
      {
        message.Created = message.Created.ToLocalTime();
      }

      PopupVisible = true;
    }
    catch (Exception ex)
    {
      ErrorMessage = $"Error loading thread messages: {ex.Message}";
    }
  }

  /// <summary>
  /// Shows the delete confirmation dialog for a thread
  /// </summary>
  /// <param name="thread">The thread to delete</param>
  private void ShowDeleteConfirmation(DuetThread thread)
  {
    ThreadToDelete = thread;
    DeleteConfirmationVisible = true;
  }

  /// <summary>
  /// Deletes a thread and its associated messages
  /// </summary>
  private async Task DeleteThreadAsync()
  {
    try
    {
      if (ThreadToDelete == null) return;

      // Get a fresh tracked instance of the thread from the database
      var threadToDelete = await Context.Threads
          .FirstOrDefaultAsync(t => t.Id == ThreadToDelete.Id);

      if (threadToDelete == null)
      {
        ErrorMessage = "Thread not found";
        return;
      }

      // Delete associated messages first
      var messages = await Context.Messages
          .Where(m => m.ThreadId == threadToDelete.Id)
          .ToListAsync();
      Context.Messages.RemoveRange(messages);

      // Delete the thread
      Context.Threads.Remove(threadToDelete);
      await Context.SaveChangesAsync();

      // Remove from grid data source
      GridDataSource.Remove(ThreadToDelete);
      ThreadToDelete = null;
      DeleteConfirmationVisible = false;

      StateHasChanged();
    }
    catch (Exception ex)
    {
      ErrorMessage = $"Error deleting thread: {ex.Message}";
    }
  }

  /// <summary>
  /// Formats a message's content using Markdig for markdown rendering
  /// </summary>
  /// <param name="content">The raw message content</param>
  /// <returns>HTML formatted content</returns>
  private string FormatMessage(string content)
  {
    if (string.IsNullOrEmpty(content))
      return string.Empty;

    var pipeline = new MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
      .Build();

    return Markdown.ToHtml(content, pipeline);
  }
}