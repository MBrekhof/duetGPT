// Types matching your .NET backend models

export interface User {
  id: string;
  email: string;
  userName: string;
}

export interface Thread {
  id: number;
  userId: string;
  title: string;
  totalTokens: number;
  totalCost: number;
  createdAt: string;
  updatedAt: string;
}

export interface Message {
  id: number;
  threadId: number;
  role: 'user' | 'assistant';
  content: string;
  tokens?: number;
  createdAt: string;
}

export interface ChatRequest {
  threadId?: number;
  message: string;
  model: string;
  enableRag: boolean;
  enableExtendedThinking: boolean;
  enableWebSearch: boolean;
  customPrompt?: string;
  attachedDocumentIds?: number[];
  imageData?: string;
}

export interface ChatResponse {
  content: string;
  thinking?: string;
  threadId: number;
  messageId: number;
  tokens: number;
  cost: number;
}

export interface Document {
  id: number;
  fileName: string;
  uploadedAt: string;
  size: number;
}

export interface Prompt {
  id: number;
  name: string;
  content: string;
}

export interface Knowledge {
  id: number;
  content: string;
  metadata?: string;
  createdAt: string;
}

export interface Model {
  id: string;
  name: string;
  provider: 'anthropic' | 'openai';
  supportsExtendedThinking?: boolean;
}
