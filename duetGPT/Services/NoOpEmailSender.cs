using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using duetGPT.Data;

namespace duetGPT.Services
{
    public class NoOpEmailSender : IEmailSender<ApplicationUser>
    {
        private readonly ILogger<NoOpEmailSender> _logger;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            try
            {
                _logger.LogInformation("Sending confirmation link to {Email}", email);
                Console.WriteLine($"Confirmation link for {email}: {confirmationLink}");
                _logger.LogDebug("Confirmation link sent successfully to {Email}", email);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation link to {Email}", email);
                throw;
            }
        }

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            try
            {
                _logger.LogInformation("Sending password reset link to {Email}", email);
                Console.WriteLine($"Password reset link for {email}: {resetLink}");
                _logger.LogDebug("Password reset link sent successfully to {Email}", email);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset link to {Email}", email);
                throw;
            }
        }

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            try
            {
                _logger.LogInformation("Sending password reset code to {Email}", email);
                Console.WriteLine($"Password reset code for {email}: {resetCode}");
                _logger.LogDebug("Password reset code sent successfully to {Email}", email);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset code to {Email}", email);
                throw;
            }
        }
    }
}
