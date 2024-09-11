using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using duetGPT.Data;

namespace duetGPT.Services
{
    public class FileUploadService
    {
        private readonly ILogger<FileUploadService> _logger;
        private readonly ApplicationDbContext _dbContext;
        private const int MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

        public FileUploadService(ILogger<FileUploadService> logger, ApplicationDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task<bool> UploadFile(IFormFile file, string userId)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    _logger.LogWarning("File is null or empty");
                    return false;
                }

                if (file.Length > MaxFileSizeBytes)
                {
                    _logger.LogWarning($"File size exceeds the maximum limit of {MaxFileSizeBytes / (1024 * 1024)} MB");
                    return false;
                }

                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);

                    var document = new Document
                    {
                        FileName = file.FileName,
                        ContentType = file.ContentType,
                        Content = memoryStream.ToArray(),
                        UploadedAt = DateTime.UtcNow,
                        OwnerId = userId
                    };

                    _dbContext.Documents.Add(document);
                    await _dbContext.SaveChangesAsync();
                }

                _logger.LogInformation($"File {file.FileName} uploaded successfully to the database");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file to the database");
                return false;
            }
        }
    }
}