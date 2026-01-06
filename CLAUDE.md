# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Documentation Management

**IMPORTANT**: All project-related documentation, plans, and notes must be kept within the project directory structure, NOT in user home directories or system locations.

- **Plans**: When using plan mode, always write plans to `PLAN.md` in the project root (not to `.claude/plans/` in user directory)
- **Implementation docs**: Keep all implementation plans, refactoring notes, and technical documentation in the project root or a `/docs` subdirectory
- **Rationale**: Ensures documentation persists across sessions, survives system reboots, and is accessible to all team members through version control

## Git Workflow

**IMPORTANT**: Follow this workflow for all git operations:

- **Commits**: Create commits freely when completing features, fixes, or logical units of work
  - Use descriptive commit messages that explain the "why" not just the "what"
  - Follow the repository's commit message style (check git log for examples)
  - Include the Claude Code signature: "🤖 Generated with [Claude Code](https://claude.com/claude-code)"

- **Pushing**: **ONLY push to remote when explicitly requested by the user**
  - Never push automatically after commits
  - Wait for explicit instruction like "push", "push to remote", or "push to GitHub"
  - This gives the user control over when changes go to the remote repository

- **Pull Requests**: Create PRs using `gh pr create` when requested
  - Follow the standard PR template with Summary and Test Plan sections

## Project Overview

duetGPT is a Blazor Server application built on .NET 9.0 that provides a chat interface for interacting with Claude (Anthropic) and OpenAI models. The application features RAG (Retrieval-Augmented Generation) capabilities using PostgreSQL with pgvector for semantic search, document processing, and extended thinking mode support.

## Build and Run Commands

```bash
# Build the solution
dotnet build duetGPT.sln

# Run the application (from duetGPT project directory)
cd duetGPT
dotnet run

# Run in watch mode for development
dotnet watch run

# Clean build artifacts
dotnet clean

# Restore packages
dotnet restore
```

## Database Commands

The application uses Entity Framework Core with PostgreSQL and automatic migrations on startup. However, you can manage migrations manually:

```bash
# Add a new migration (from duetGPT project directory)
dotnet ef migrations add MigrationName

# Update database manually
dotnet ef database update

# Remove last migration
dotnet ef migrations remove
```

Note: Database migrations are automatically applied on application startup (see Program.cs:112-125).

## Architecture

### Core Services

**AnthropicService** (`Services/AnthropicService.cs`)
- Manages Anthropic API client initialization and message handling
- Supports extended thinking mode with Claude 3.7 Sonnet
- Uses Anthropic.SDK (v5.6.0) for API communication
- Converts between custom ExtendedMessageRequest/Response models and SDK types

**OpenAIService** (`Services/OpenAIService.cs`)
- Handles OpenAI API interactions
- Generates embeddings for the RAG system using text-embedding-3-small (1536 dimensions)
- Tracks token usage and costs

**KnowledgeService** (`Services/KnowledgeService.cs`)
- Implements RAG functionality using PostgreSQL pgvector extension
- Performs semantic search with cosine distance similarity
- Includes metadata-based relevance boosting for headers and key phrases
- Tracks embedding costs per knowledge item
- Uses distance threshold filtering (MaxDistanceThreshold = 0.3)

**DocumentProcessingService** (`Services/DocumentProcessingService.cs`)
- Extracts text from PDF (using DevExpress.Pdf) and DOCX/DOC (using DevExpress.RichEdit)
- Implements intelligent chunking with overlap for better context preservation
- Extracts structural metadata (headers, lists, key phrases) to enhance RAG accuracy
- Chunk size is configurable via token limits with 20% overlap between chunks

### Data Models

**ApplicationDbContext** (`Data/ApplicationDbContext.cs`)
- Uses PostgreSQL with pgvector extension enabled
- Snake_case naming convention via EFCore.NamingConventions
- Configured with DbContextFactory for Blazor Server scoped usage

**Key Entities:**
- `DuetThread`: Conversation threads with user tracking and cost/token metrics
- `DuetMessage`: Individual messages within threads (cascade delete)
- `Knowledge`: RAG knowledge base with pgvector embeddings (1536 dimensions)
- `Document`: File uploads with content stored as byte arrays
- `Prompt`: Custom system prompts users can select
- `ThreadDocument`: Many-to-many relationship between threads and documents

### Blazor Components

**Claude.razor** (`Components/Pages/Claude.razor`)
- Main chat interface with model selection (Anthropic and OpenAI models)
- Supports extended thinking mode visualization
- Document attachment to threads for context
- Custom prompt selection
- Web search toggle (for future implementation)
- Real-time token and cost tracking

**KnowledgePage.razor** - Manages knowledge base entries
**Files.razor** - Document upload and management
**Messages.razor** - Thread history and management

## Configuration

**appsettings.json** structure:
```json
{
  "Anthropic": {
    "ApiKey": "your-key-here"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=duetgpt;Username=postgres;Password=..."
  },
  "Serilog": { ... }
}
```

Configuration requirements:
- Anthropic API key must be set in configuration
- PostgreSQL connection string pointing to a database with pgvector extension enabled
- OpenAI API key should be configured for embedding generation

## Key Dependencies

- **Anthropic.SDK** (v5.6.0): Claude API integration
- **OpenAI-DotNet** (v8.8.2): OpenAI API integration
- **DevExpress.Blazor** (v25.2.3): UI components
- **Npgsql.EntityFrameworkCore.PostgreSQL** (v9.0.4): PostgreSQL provider
- **Pgvector** (v0.3.2) + **Pgvector.EntityFrameworkCore** (v0.2.2): Vector similarity search
- **Serilog.AspNetCore** (v9.0.0): Structured logging

## Important Notes

### RAG Implementation
The knowledge service uses a sophisticated chunking strategy:
1. Documents are normalized and split into structural elements (headers, paragraphs, lists)
2. Metadata is extracted including type, length, importance, and key phrases
3. Overlapping chunks are created (20% overlap) to preserve context across boundaries
4. Semantic search results are boosted based on metadata (headers, key phrases)
5. Results filtered by distance threshold to ensure relevance

### Extended Thinking Mode
- Only available with Claude 3.7 Sonnet model (check `IsExtendedThinkingAvailable()`)
- Thinking content is returned separately in response.Content as ThinkingContent type
- Budget tokens can be specified in ThinkingParameters

### Authentication
Uses ASP.NET Core Identity with custom `ApplicationUser` extending `IdentityUser`. Email sender is stubbed with `NoOpEmailSender` for development.

### File Uploads
- Maximum file size: 50 MB (configured in Program.cs)
- Supported formats: PDF, DOCX, DOC
- Endpoint: POST `/api/UploadValidation/Upload`
- Files stored in database as byte arrays in `Document.Content`

### Logging
Serilog configured for file and console output. Logs rotate daily with 31-day retention and 10MB size limit per file.
