using Microsoft.AspNetCore.Identity;
using duetGPT.Data;

namespace duetGPT.Services
{
    public class NoOpEmailSender : IEmailSender<ApplicationUser>
    {
        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            Console.WriteLine($"Confirmation link for {email}: {confirmationLink}");
            return Task.CompletedTask;
        }

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            Console.WriteLine($"Password reset link for {email}: {resetLink}");
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            Console.WriteLine($"Password reset code for {email}: {resetCode}");
            return Task.CompletedTask;
        }
    }
}