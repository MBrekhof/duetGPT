# duetGPT

[![Playwright E2E Tests](https://github.com/MBrekhof/duetGPT/actions/workflows/playwright-tests.yml/badge.svg)](https://github.com/MBrekhof/duetGPT/actions/workflows/playwright-tests.yml)

A modern Blazor Server application for interacting with Claude (Anthropic) and OpenAI models, featuring RAG (Retrieval-Augmented Generation) capabilities with PostgreSQL pgvector for semantic search.

## Features

- 🤖 **Multi-Model Support**: Chat with Claude (Anthropic) and OpenAI models
- 🧠 **Extended Thinking Mode**: Support for Claude 3.7 Sonnet's extended thinking capabilities
- 📚 **RAG System**: Semantic search using PostgreSQL with pgvector extension
- 📄 **Document Processing**: Upload and process PDF, DOCX, and DOC files
- 💬 **Modern Chat UI**: Built with DevExpress DxAIChat component
- 🎨 **Custom Prompts**: Create and use custom system prompts
- 🔍 **Web Search Integration**: Optional web search toggle (planned)
- 📊 **Token Tracking**: Real-time token usage and cost monitoring
- 🖼️ **Image Support**: Upload and include images in conversations
- 🔐 **Authentication**: ASP.NET Core Identity with user management

## Tech Stack

- **.NET 9.0** - Modern C# with Blazor Server
- **PostgreSQL + pgvector** - Vector database for semantic search
- **Anthropic.SDK** - Claude API integration
- **OpenAI-DotNet** - OpenAI API integration
- **DevExpress Blazor** - Enterprise UI components
- **Entity Framework Core** - Database ORM
- **Serilog** - Structured logging
- **Playwright** - End-to-end testing

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- PostgreSQL 15+ with pgvector extension
- Anthropic API key
- OpenAI API key (optional, for embeddings)

### Configuration

1. Update `appsettings.json`:

```json
{
  "Anthropic": {
    "ApiKey": "your-anthropic-api-key"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=duetgpt;Username=postgres;Password=your-password"
  }
}
```

2. Enable pgvector extension in PostgreSQL:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

### Running the Application

```bash
# Clone the repository
git clone https://github.com/MBrekhof/duetGPT.git
cd duetGPT

# Restore dependencies
dotnet restore

# Run the application
cd duetGPT
dotnet run

# Navigate to https://localhost:44391
```

The application will automatically apply database migrations on startup.

## Testing

The project includes comprehensive end-to-end tests using Playwright.

### Run Tests Locally

```bash
# Terminal 1 - Start the application
cd duetGPT
dotnet run

# Terminal 2 - Run tests
cd duetGPT.Tests
dotnet test
```

Or use the PowerShell script:

```powershell
.\run-tests.ps1
```

### Test Coverage

- ✅ Authentication and authorization
- ✅ Chat page loading
- ✅ Message sending
- ✅ New thread creation
- ✅ RAG toggle visibility
- ✅ Model selection

All tests run automatically via GitHub Actions on every push.

## Project Structure

```
duetGPT/
├── Components/
│   ├── Pages/          # Blazor pages (ClaudeV2.razor, etc.)
│   └── Account/        # Authentication components
├── Services/           # Business logic
│   ├── AnthropicService.cs
│   ├── OpenAIService.cs
│   ├── KnowledgeService.cs
│   └── DocumentProcessingService.cs
├── Data/              # Entity models and DbContext
└── Migrations/        # EF Core migrations

duetGPT.Tests/         # Playwright E2E tests
└── *.cs              # Test files

.github/
└── workflows/        # GitHub Actions CI/CD
```

## RAG Implementation

The application uses a sophisticated RAG system:

1. **Document Processing**: Intelligent chunking with overlap for context preservation
2. **Embeddings**: Generated using OpenAI text-embedding-3-small (1536 dimensions)
3. **Semantic Search**: PostgreSQL pgvector with cosine distance similarity
4. **Metadata Boosting**: Enhanced relevance for headers and key phrases
5. **Distance Filtering**: Results filtered by threshold (MaxDistanceThreshold = 0.3)

## Extended Thinking Mode

Claude 3.7 Sonnet supports extended thinking mode, where the model's reasoning process is visible:

- Toggle "Enable Extended Thinking" in the UI
- View thinking content in a dedicated popup
- Configure thinking budget tokens

## Development

### Build the Solution

```bash
dotnet build
```

### Watch Mode (Hot Reload)

```bash
cd duetGPT
dotnet watch run
```

### Database Migrations

```bash
# Add a new migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update

# Remove last migration
dotnet ef migrations remove
```

## Contributing

Contributions are welcome! Please ensure:

1. All tests pass locally
2. Code follows existing patterns
3. Commit messages are descriptive
4. GitHub Actions workflow passes

## Documentation

- [CLAUDE.md](CLAUDE.md) - Guidance for Claude Code
- [TESTING.md](TESTING.md) - Detailed testing documentation
- [.github/workflows/README.md](.github/workflows/README.md) - CI/CD documentation

## License

[Your License Here]

## Acknowledgments

- Built with [Claude Code](https://claude.com/claude-code)
- Uses [DevExpress Blazor Components](https://www.devexpress.com/blazor/)
- Powered by [Anthropic Claude](https://www.anthropic.com/)
