using Microsoft.EntityFrameworkCore;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        private void UpdateTokensAsync(int value)
        {
            try
            {
                if (_tokens != value)
                {
                    _tokens = value;
                    if (currentThread != null)
                    {
                        currentThread.TotalTokens = _tokens;
                         DbContext.SaveChanges();
                        Logger.LogInformation("Updated tokens for thread {ThreadId}: {Tokens}", currentThread.Id, _tokens);
                    }
                    StateHasChanged();
                }
            }
            catch (DbUpdateException ex)
            {
                Logger.LogError(ex, "Database error while updating tokens");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error updating tokens");
                throw;
            }
        }

        private void UpdateCostAsync(decimal value)
        {
            try
            {
                if (_cost != value)
                {
                    _cost = value;
                    if (currentThread != null)
                    {
                        currentThread.Cost = _cost;
                        DbContext.SaveChanges();
                        Logger.LogInformation("Updated cost for thread {ThreadId}: {Cost}", currentThread.Id, _cost);
                    }
                    StateHasChanged();
                }
            }
            catch (DbUpdateException ex)
            {
                Logger.LogError(ex, "Database error while updating cost");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error updating cost");
                throw;
            }
        }
    }
}
