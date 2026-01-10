# duetGPT

[![Playwright E2E Tests](https://github.com/MBrekhof/duetGPT/actions/workflows/playwright-tests.yml/badge.svg)](https://github.com/MBrekhof/duetGPT/actions/workflows/playwright-tests.yml)

A modern chat application with dual frontend options for interacting with Claude 4.5 (Anthropic) models, featuring RAG (Retrieval-Augmented Generation) capabilities with PostgreSQL pgvector for semantic search.

## 🎨 Dual Frontend Architecture

duetGPT offers two frontend options:

### Next.js Frontend (Modern, Recommended)
- **Location**: `duetgpt-web/`
- **Tech**: React, TypeScript, Next.js 14 App Router, Tailwind CSS
- **Features**: Modern UI, JWT authentication, responsive design
- **URL**: http://localhost:3000

### Blazor Frontend (Classic)
- **Location**: `duetGPT/Components/`
- **Tech**: .NET 9 Blazor Server, DevExpress Components
- **Features**: Real-time updates, server-side rendering
- **URL**: https://localhost:44391

Both frontends connect to the same .NET backend with shared services and data.

## Features

### Core Capabilities
- 🤖 **Claude 4.5 Models**: Chat with latest Anthropic models
  - Claude Sonnet 4.5 (balanced)
  - Claude Haiku 4.5 (fastest)
  - Claude Opus 4.5 (most intelligent)
- 🧠 **RAG System**: Semantic search using PostgreSQL with pgvector extension
- 📄 **Document Processing**: Upload and process PDF, DOCX, and DOC files
- 💬 **Thread Management**: Organize conversations with automatic title generation
- 📊 **Token Tracking**: Real-time token usage and cost monitoring
- 🔐 **Authentication**: Cookie-based (Blazor) and JWT token (Next.js) support

### Next.js Features
- Modern chat interface with thread sidebar
- Model selection dropdown
- RAG toggle for knowledge base integration
- Login/register pages
- Responsive design

### Blazor Features
- Extended thinking mode visualization
- Document attachment to threads
- Custom prompt selection
- Web search integration (planned)
- Image upload support

## Tech Stack

### Backend (.NET 9)
- **ASP.NET Core** - Web framework
- **PostgreSQL + pgvector** - Vector database for semantic search
- **Anthropic.SDK** (v5.6.0) - Claude API integration
- **OpenAI-DotNet** (v8.8.2) - Embeddings generation
- **Entity Framework Core** - Database ORM
- **Serilog** - Structured logging

### Frontend (Next.js)
- **Next.js 14** - React framework with App Router
- **TypeScript** - Type safety
- **Tailwind CSS** - Styling
- **Axios** - HTTP client

### Frontend (Blazor)
- **Blazor Server** - Real-time UI framework
- **DevExpress Blazor** (v25.2.3) - Enterprise UI components
- **ASP.NET Core Identity** - Authentication

### Infrastructure
- **Docker** - PostgreSQL containerization
- **Playwright** - End-to-end testing

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Node.js 18+ (for Next.js frontend)
- Docker (for PostgreSQL)
- Anthropic API key
- OpenAI API key (optional, for embeddings)

### Quick Start with Docker

1. **Clone the repository**
```bash
git clone https://github.com/MBrekhof/duetGPT.git
cd duetGPT
```

2. **Start PostgreSQL with Docker**
```bash
docker-compose up -d
```

3. **Configure the backend**

Create `appsettings.json`:
```json
{
  "Anthropic": {
    "ApiKey": "your-anthropic-api-key"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=duetgpt;Username=postgres;Password=1Zaqwsx2"
  },
  "JwtSettings": {
    "SecretKey": "YourSuperSecretKeyThatIsAtLeast32CharactersLong!",
    "Issuer": "duetGPT",
    "Audience": "duetGPT-users"
  }
}
```

4. **Run database migrations**
```bash
cd duetGPT
dotnet ef database update
```

5. **Start the backend**
```bash
dotnet run
# Backend runs at https://localhost:44391
```

### Option A: Next.js Frontend

In a new terminal:
```bash
cd duetgpt-web
npm install
npm run dev
# Frontend runs at http://localhost:3000
```

Visit http://localhost:3000, register an account, and start chatting!

### Option B: Blazor Frontend

Simply navigate to https://localhost:44391 after starting the backend.

## Project Structure

```
duetGPT/
├── duetGPT/                    # .NET Backend
│   ├── Controllers/            # API endpoints for Next.js
│   │   ├── AuthController.cs   # JWT authentication
│   │   ├── ChatController.cs   # Chat messages
│   │   ├── ThreadsController.cs # Thread management
│   │   ├── DocumentsController.cs
│   │   ├── KnowledgeController.cs
│   │   └── PromptsController.cs
│   ├── Services/               # Business logic
│   │   ├── AnthropicService.cs # Claude API wrapper
│   │   ├── ChatMessageService.cs
│   │   ├── ThreadService.cs
│   │   ├── KnowledgeService.cs # RAG implementation
│   │   ├── DocumentProcessingService.cs
│   │   └── ImageService.cs
│   ├── Data/                   # EF Core models
│   │   ├── ApplicationDbContext.cs
│   │   └── Models/
│   ├── Components/             # Blazor UI
│   │   ├── Pages/
│   │   └── Layout/
│   └── Program.cs              # Startup configuration
│
├── duetgpt-web/                # Next.js Frontend
│   ├── app/                    # App Router pages
│   │   ├── page.tsx            # Home (chat)
│   │   ├── login/
│   │   └── register/
│   ├── components/             # React components
│   │   ├── chat/
│   │   │   ├── ChatInterface.tsx
│   │   │   ├── MessageList.tsx
│   │   │   └── MessageInput.tsx
│   │   └── ui/
│   ├── lib/
│   │   └── api-client.ts       # Backend API client
│   ├── types/
│   │   └── index.ts            # TypeScript definitions
│   └── package.json
│
├── duetGPT.Tests/              # Playwright E2E tests
├── docker-compose.yml          # PostgreSQL container
└── .github/workflows/          # CI/CD
```

## Database Setup

The application uses PostgreSQL with the pgvector extension for semantic search.

### Using Docker (Recommended)

```bash
# Start PostgreSQL container
docker-compose up -d

# Verify container is running
docker ps

# Apply migrations
cd duetGPT
dotnet ef database update
```

### Manual PostgreSQL Setup

If not using Docker:

```sql
-- Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;
```

Update `appsettings.json` with your PostgreSQL connection string.

## RAG Implementation

The knowledge service uses a sophisticated chunking strategy:

1. **Document Processing**: Documents are normalized and split into structural elements (headers, paragraphs, lists)
2. **Metadata Extraction**: Type, length, importance, and key phrases are extracted
3. **Overlapping Chunks**: 20% overlap to preserve context across boundaries
4. **Embeddings**: Generated using OpenAI text-embedding-3-small (1536 dimensions)
5. **Semantic Search**: PostgreSQL pgvector with cosine distance similarity
6. **Metadata Boosting**: Results are boosted based on headers and key phrases
7. **Distance Filtering**: Results filtered by threshold (MaxDistanceThreshold = 0.3)

## Authentication

### Blazor Frontend
- Cookie-based authentication via ASP.NET Core Identity
- Traditional login/register pages at `/Account/Login` and `/Account/Register`

### Next.js Frontend
- JWT token authentication
- Tokens stored in localStorage
- Automatic token injection via Axios interceptors
- Login/register pages at `/login` and `/register`

### Backend Authorization
Supports both authentication schemes:
```csharp
options.DefaultPolicy = new AuthorizationPolicyBuilder()
    .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .Build();
```

## API Endpoints

Base URL: `https://localhost:44391/api`

### Authentication
- `POST /auth/login` - Login with email/password, returns JWT token
- `POST /auth/register` - Register new user
- `POST /auth/logout` - Logout

### Threads
- `GET /threads` - List all threads
- `GET /threads/{id}` - Get thread by ID
- `POST /threads` - Create new thread
- `DELETE /threads/{id}` - Delete thread
- `GET /threads/{id}/messages` - Get messages in thread

### Chat
- `POST /chat` - Send message and get response
  - Supports RAG, custom prompts, document attachments
  - Request body: `{ threadId, message, model, enableRag }`

### Documents
- `GET /documents` - List uploaded documents
- `POST /documents/upload` - Upload document (PDF, DOCX, DOC)
- `DELETE /documents/{id}` - Delete document

### Knowledge
- `GET /knowledge` - List knowledge base entries
- `POST /knowledge` - Add knowledge entry
- `DELETE /knowledge/{id}` - Delete entry

### Prompts
- `GET /prompts` - List custom prompts

## Development

### Backend Development

```bash
# Build solution
dotnet build

# Run with hot reload
cd duetGPT
dotnet watch run

# Run tests
cd duetGPT.Tests
dotnet test

# Add migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update
```

### Frontend Development (Next.js)

```bash
cd duetgpt-web

# Install dependencies
npm install

# Run dev server with hot reload
npm run dev

# Build for production
npm run build

# Lint code
npm run lint
```

## Testing

The project includes comprehensive end-to-end tests using Playwright.

### Run Tests

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

## Configuration

### Environment Variables

**Backend** (`appsettings.json`):
- `Anthropic:ApiKey` - Anthropic API key (required)
- `ConnectionStrings:DefaultConnection` - PostgreSQL connection
- `JwtSettings:SecretKey` - JWT signing key (change in production)
- `JwtSettings:Issuer` - JWT issuer
- `JwtSettings:Audience` - JWT audience

**Next.js Frontend** (`duetgpt-web/.env.local`):
```env
NEXT_PUBLIC_API_URL=https://localhost:44391
```

### CORS Configuration

The backend is configured to accept requests from:
- http://localhost:3000 (Next.js development)

Update `Program.cs` to add production URLs:
```csharp
policy.WithOrigins("http://localhost:3000", "https://your-production-url.com")
```

## Deployment

### Backend (.NET)

```bash
# Publish for production
dotnet publish -c Release -o ./publish

# Run published app
cd publish
dotnet duetGPT.dll
```

Deploy to:
- Azure App Service
- AWS Elastic Beanstalk
- Any host supporting .NET 9

### Frontend (Next.js)

```bash
cd duetgpt-web

# Build for production
npm run build

# Start production server
npm start
```

Deploy to:
- Vercel (recommended for Next.js)
- Netlify
- Azure Static Web Apps
- Any Node.js hosting

### Database

For production:
1. Use managed PostgreSQL service (Azure Database, AWS RDS, etc.)
2. Ensure pgvector extension is enabled
3. Update connection string in production configuration
4. Run migrations: `dotnet ef database update`

## Security Considerations

### Important Production Changes

1. **JWT Secret**: Use a strong, randomly generated secret key
2. **API Keys**: Store in environment variables or Azure Key Vault, never in source control
3. **HTTPS**: Enforce HTTPS in production
4. **CORS**: Restrict to specific production domains
5. **Rate Limiting**: Implement rate limiting on API endpoints
6. **Input Validation**: All endpoints have validation
7. **SQL Injection**: Using EF Core with parameterized queries

### Secrets Management

Never commit sensitive data. Use:
- `appsettings.Development.json` for local development (gitignored)
- Environment variables for production
- Azure Key Vault or AWS Secrets Manager for cloud deployments

## Troubleshooting

### Database Connection Issues

```bash
# Check PostgreSQL is running
docker ps

# View logs
docker logs duetgpt-postgres

# Restart container
docker-compose restart
```

### CORS Errors

1. Ensure backend is running
2. Check CORS policy includes your frontend URL
3. Restart backend after configuration changes

### Authentication Issues

**401 Unauthorized**:
- JWT token expired or invalid
- Clear localStorage and login again
- Check JWT settings in backend configuration

**Cookie not working (Blazor)**:
- Clear browser cookies
- Check HTTPS is used for production

### Build Errors

```bash
# Clean and restore
dotnet clean
dotnet restore
cd duetgpt-web && npm ci
```

## Contributing

Contributions are welcome! Please ensure:

1. All tests pass locally: `dotnet test`
2. Code follows existing patterns
3. Commit messages are descriptive
4. Include the Claude Code signature

## Documentation

- [CLAUDE.md](CLAUDE.md) - Guidance for Claude Code when working with this codebase
- [NEXT_STEPS.md](NEXT_STEPS.md) - Planned UI improvements and future enhancements

## License

[Your License Here]

## Acknowledgments

- Built with [Claude Code](https://claude.com/claude-code)
- Powered by [Anthropic Claude](https://www.anthropic.com/)
- UI components from [DevExpress Blazor](https://www.devexpress.com/blazor/) and [Tailwind CSS](https://tailwindcss.com/)
