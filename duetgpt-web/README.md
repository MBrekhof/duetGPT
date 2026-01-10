# duetGPT Web Frontend

Modern Next.js 14 frontend for duetGPT, communicating with the .NET backend API.

## Features

- Modern, responsive chat interface
- Support for multiple AI models (Claude, GPT-4)
- RAG (Retrieval-Augmented Generation) toggle
- Extended thinking mode for Claude 3.7 Sonnet
- Web search integration
- Thread management (create, switch, delete)
- Real-time message streaming
- Dark mode support

## Tech Stack

- **Next.js 14** - React framework with App Router
- **TypeScript** - Type-safe development
- **Tailwind CSS** - Utility-first styling
- **Axios** - HTTP client for API communication
- **React Markdown** - Markdown rendering for AI responses
- **Lucide React** - Icon library

## Prerequisites

- Node.js 18+ and npm
- .NET backend running at `https://localhost:44391` (or configure `NEXT_PUBLIC_API_URL`)

## Getting Started

1. **Install dependencies:**
   ```bash
   npm install
   ```

2. **Configure environment variables:**

   Copy `.env.local.example` to `.env.local` and update with your backend URL:
   ```bash
   cp .env.local.example .env.local
   ```

   Edit `.env.local`:
   ```env
   NEXT_PUBLIC_API_URL=https://localhost:44391
   ```

3. **Run the development server:**
   ```bash
   npm run dev
   ```

4. **Open your browser:**

   Navigate to [http://localhost:3000](http://localhost:3000)

## Project Structure

```
duetgpt-web/
├── app/                    # Next.js App Router
│   ├── layout.tsx         # Root layout
│   ├── page.tsx           # Home page (chat interface)
│   └── globals.css        # Global styles with Tailwind
├── components/
│   ├── chat/              # Chat-related components
│   │   ├── ChatInterface.tsx  # Main chat container
│   │   ├── MessageList.tsx    # Message display
│   │   └── MessageInput.tsx   # Message input
│   └── ui/                # Reusable UI components
│       └── Button.tsx     # Button component
├── lib/
│   └── api-client.ts      # API client for backend communication
├── types/
│   └── index.ts           # TypeScript type definitions
└── public/                # Static assets
```

## API Integration

The frontend communicates with the .NET backend through the `ApiClient` class in `lib/api-client.ts`. It handles:

- Authentication (JWT tokens in localStorage)
- Thread management (CRUD operations)
- Message sending and retrieval
- Document uploads
- Knowledge base queries
- Model selection

## Authentication

The app uses JWT token authentication stored in `localStorage`. When you log in through the backend, the token is automatically saved and included in all subsequent requests via the `Authorization` header.

## Available Scripts

- `npm run dev` - Start development server
- `npm run build` - Build for production
- `npm run start` - Start production server
- `npm run lint` - Run ESLint

## Backend API Endpoints

The frontend expects these endpoints from the .NET backend:

- `POST /api/auth/login` - Login
- `POST /api/auth/register` - Register
- `POST /api/auth/logout` - Logout
- `GET /api/threads` - Get all threads
- `GET /api/threads/:id` - Get thread by ID
- `POST /api/threads` - Create thread
- `DELETE /api/threads/:id` - Delete thread
- `GET /api/threads/:id/messages` - Get messages for thread
- `POST /api/chat` - Send message
- `GET /api/documents` - Get documents
- `POST /api/documents/upload` - Upload document
- `DELETE /api/documents/:id` - Delete document
- `GET /api/prompts` - Get prompts
- `GET /api/knowledge` - Get knowledge entries
- `POST /api/knowledge` - Save knowledge

## Styling

The app uses Tailwind CSS for styling with support for dark mode. The color scheme automatically adapts to the user's system preferences.

## Development Notes

- The app uses Next.js App Router (not Pages Router)
- All API calls go through the singleton `apiClient` instance
- TypeScript types are defined to match the .NET backend models
- The chat interface is fully client-side rendered (`'use client'` directive)

## Future Enhancements

- Authentication UI (login/register pages)
- Document attachment to messages
- Custom prompt selection UI
- Settings panel for API configuration
- Message editing and regeneration
- Export conversation history
- Streaming responses for real-time updates

## Troubleshooting

**API Connection Issues:**
- Ensure the .NET backend is running at the configured URL
- Check CORS settings in the backend to allow requests from `http://localhost:3000`
- Verify SSL certificate if using HTTPS

**Build Errors:**
- Run `npm install` to ensure all dependencies are installed
- Clear `.next` folder and rebuild: `rm -rf .next && npm run build`

**Type Errors:**
- Ensure TypeScript types in `types/index.ts` match the backend models
- Run `npm run lint` to check for issues

## License

This project is part of the duetGPT application.
