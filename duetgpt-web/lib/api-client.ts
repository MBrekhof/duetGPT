import axios, { AxiosInstance } from 'axios';
import type {
  Thread,
  Message,
  ChatRequest,
  ChatResponse,
  Document,
  Prompt,
  Knowledge,
  Model
} from '@/types';

class ApiClient {
  private client: AxiosInstance;

  constructor() {
    this.client = axios.create({
      baseURL: process.env.NEXT_PUBLIC_API_URL || 'https://localhost:44391',
      headers: {
        'Content-Type': 'application/json',
      },
      // Important: allows cookies for authentication
      withCredentials: true,
    });

    // Add request interceptor for authentication token
    this.client.interceptors.request.use((config) => {
      const token = this.getAuthToken();
      if (token) {
        config.headers.Authorization = `Bearer ${token}`;
      }
      return config;
    });
  }

  private getAuthToken(): string | null {
    if (typeof window !== 'undefined') {
      return localStorage.getItem('auth_token');
    }
    return null;
  }

  public setAuthToken(token: string) {
    if (typeof window !== 'undefined') {
      localStorage.setItem('auth_token', token);
    }
  }

  public clearAuthToken() {
    if (typeof window !== 'undefined') {
      localStorage.removeItem('auth_token');
    }
  }

  // Authentication
  async login(email: string, password: string) {
    const response = await this.client.post('/api/auth/login', { email, password });
    if (response.data.token) {
      this.setAuthToken(response.data.token);
    }
    return response.data;
  }

  async register(email: string, password: string, confirmPassword: string) {
    const response = await this.client.post('/api/auth/register', {
      email,
      password,
      confirmPassword
    });
    return response.data;
  }

  async logout() {
    await this.client.post('/api/auth/logout');
    this.clearAuthToken();
  }

  // Threads
  async getThreads(): Promise<Thread[]> {
    const response = await this.client.get('/api/threads');
    return response.data;
  }

  async getThread(id: number): Promise<Thread> {
    const response = await this.client.get(`/api/threads/${id}`);
    return response.data;
  }

  async createThread(title: string): Promise<Thread> {
    const response = await this.client.post('/api/threads', { title });
    return response.data;
  }

  async deleteThread(id: number): Promise<void> {
    await this.client.delete(`/api/threads/${id}`);
  }

  // Messages
  async getMessages(threadId: number): Promise<Message[]> {
    const response = await this.client.get(`/api/threads/${threadId}/messages`);
    return response.data;
  }

  async sendMessage(request: ChatRequest): Promise<ChatResponse> {
    const response = await this.client.post('/api/chat', request);
    return response.data;
  }

  // Documents
  async getDocuments(): Promise<Document[]> {
    const response = await this.client.get('/api/documents');
    return response.data;
  }

  async uploadDocument(file: File): Promise<Document> {
    const formData = new FormData();
    formData.append('file', file);
    const response = await this.client.post('/api/documents/upload', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });
    return response.data;
  }

  async deleteDocument(id: number): Promise<void> {
    await this.client.delete(`/api/documents/${id}`);
  }

  // Prompts
  async getPrompts(): Promise<Prompt[]> {
    const response = await this.client.get('/api/prompts');
    return response.data;
  }

  // Knowledge
  async getKnowledge(): Promise<Knowledge[]> {
    const response = await this.client.get('/api/knowledge');
    return response.data;
  }

  async saveKnowledge(content: string, metadata?: string): Promise<Knowledge> {
    const response = await this.client.post('/api/knowledge', { content, metadata });
    return response.data;
  }

  // Models - Claude 4.5 models only (GPT models are used for embeddings, not chat)
  async getModels(): Promise<Model[]> {
    // Latest Claude 4.5 models as of January 2026
    return [
      { id: 'claude-sonnet-4-5-20250929', name: 'Claude Sonnet 4.5', provider: 'anthropic', supportsExtendedThinking: true },
      { id: 'claude-haiku-4-5-20251001', name: 'Claude Haiku 4.5 (Fastest)', provider: 'anthropic', supportsExtendedThinking: true },
      { id: 'claude-opus-4-5-20251101', name: 'Claude Opus 4.5 (Most Intelligent)', provider: 'anthropic', supportsExtendedThinking: true },
    ];
  }
}

export const apiClient = new ApiClient();
