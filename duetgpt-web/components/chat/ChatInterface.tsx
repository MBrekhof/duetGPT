'use client';

import React, { useState, useEffect } from 'react';
import { apiClient } from '@/lib/api-client';
import type { Thread, Message, Model, ChatRequest } from '@/types';
import MessageList from './MessageList';
import MessageInput from './MessageInput';
import Button from '@/components/ui/Button';
import { Plus, Settings } from 'lucide-react';

export default function ChatInterface() {
  const [threads, setThreads] = useState<Thread[]>([]);
  const [currentThread, setCurrentThread] = useState<Thread | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [models, setModels] = useState<Model[]>([]);
  const [selectedModel, setSelectedModel] = useState<string>('claude-sonnet-4-5-20250929');
  const [loading, setLoading] = useState(false);
  const [enableRag, setEnableRag] = useState(false);

  useEffect(() => {
    loadInitialData();
  }, []);

  useEffect(() => {
    if (currentThread) {
      loadMessages(currentThread.id);
    }
  }, [currentThread]);

  const loadInitialData = async () => {
    try {
      const [threadsData, modelsData] = await Promise.all([
        apiClient.getThreads(),
        apiClient.getModels(),
      ]);
      setThreads(Array.isArray(threadsData) ? threadsData : []);
      setModels(Array.isArray(modelsData) ? modelsData : []);

      if (Array.isArray(threadsData) && threadsData.length > 0) {
        setCurrentThread(threadsData[0]);
      }
    } catch (error) {
      console.error('Failed to load initial data:', error);
      setThreads([]); // Ensure threads is always an array
      setModels([]); // Ensure models is always an array
    }
  };

  const loadMessages = async (threadId: number) => {
    try {
      const messagesData = await apiClient.getMessages(threadId);
      setMessages(Array.isArray(messagesData) ? messagesData : []);
    } catch (error) {
      console.error('Failed to load messages:', error);
      setMessages([]); // Ensure messages is always an array
    }
  };

  const handleSendMessage = async (content: string) => {
    if (!content.trim()) return;

    setLoading(true);
    try {
      console.log('Sending message:', content);
      const request: ChatRequest = {
        threadId: currentThread?.id,
        message: content,
        model: selectedModel,
        enableRag,
        enableExtendedThinking: false,
        enableWebSearch: false,
      };

      console.log('Request:', request);
      const response = await apiClient.sendMessage(request);
      console.log('Response:', response);

      // If no thread was selected, create a new one
      if (!currentThread) {
        console.log('Creating new thread');
        const newThread = await apiClient.getThread(response.threadId);
        setCurrentThread(newThread);
        setThreads([newThread, ...threads]);
      }

      // Add the new messages
      console.log('Loading messages for thread:', response.threadId);
      await loadMessages(response.threadId);
      console.log('Messages loaded successfully');
    } catch (error: any) {
      console.error('Failed to send message:', error);
      console.error('Error details:', error.response?.data);
      alert(`Failed to send message: ${error.response?.data?.message || error.message || 'Unknown error'}`);
    } finally {
      setLoading(false);
    }
  };

  const handleNewThread = async () => {
    try {
      const newThread = await apiClient.createThread('New Chat');
      setThreads([newThread, ...threads]);
      setCurrentThread(newThread);
      setMessages([]);
    } catch (error) {
      console.error('Failed to create thread:', error);
    }
  };

  const handleDeleteThread = async (threadId: number) => {
    try {
      await apiClient.deleteThread(threadId);
      setThreads(threads.filter(t => t.id !== threadId));
      if (currentThread?.id === threadId) {
        setCurrentThread(threads[0] || null);
      }
    } catch (error) {
      console.error('Failed to delete thread:', error);
    }
  };

  return (
    <div className="flex h-screen bg-white dark:bg-gray-900">
      {/* Sidebar */}
      <div className="w-64 border-r border-gray-200 dark:border-gray-700 flex flex-col">
        <div className="p-4 border-b border-gray-200 dark:border-gray-700">
          <Button onClick={handleNewThread} className="w-full">
            <Plus className="w-4 h-4 mr-2" />
            New Chat
          </Button>
        </div>

        <div className="flex-1 overflow-y-auto p-2">
          {threads.map((thread) => (
            <button
              key={thread.id}
              onClick={() => setCurrentThread(thread)}
              className={`w-full text-left p-3 rounded-lg mb-2 transition-colors ${
                currentThread?.id === thread.id
                  ? 'bg-blue-100 dark:bg-blue-900 text-blue-900 dark:text-blue-100'
                  : 'hover:bg-gray-100 dark:hover:bg-gray-800 text-gray-700 dark:text-gray-300'
              }`}
            >
              <div className="font-medium truncate">{thread.title}</div>
              <div className="text-xs text-gray-500 dark:text-gray-400">
                {thread.updatedAt ? new Date(thread.updatedAt).toLocaleDateString() : 'Recent'}
              </div>
            </button>
          ))}
        </div>
      </div>

      {/* Main Chat Area */}
      <div className="flex-1 flex flex-col">
        {/* Header */}
        <div className="border-b border-gray-200 dark:border-gray-700 p-4">
          <div className="flex items-center justify-between">
            <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
              {currentThread?.title || 'Select or create a chat'}
            </h1>
            <div className="flex items-center gap-4">
              <select
                value={selectedModel}
                onChange={(e) => setSelectedModel(e.target.value)}
                className="rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100"
              >
                {models.map((model) => (
                  <option key={model.id} value={model.id}>
                    {model.name}
                  </option>
                ))}
              </select>

              <button className="text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100">
                <Settings className="w-5 h-5" />
              </button>
            </div>
          </div>

          {/* Options */}
          <div className="flex gap-4 mt-3">
            <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
              <input
                type="checkbox"
                checked={enableRag}
                onChange={(e) => setEnableRag(e.target.checked)}
                className="rounded"
              />
              Enable RAG (Knowledge Base)
            </label>
          </div>
        </div>

        {/* Messages */}
        {messages.length > 0 ? (
          <MessageList messages={messages} />
        ) : (
          <div className="flex-1 flex items-center justify-center text-gray-500 dark:text-gray-400">
            <p>Start a conversation by sending a message</p>
          </div>
        )}

        {/* Input */}
        <MessageInput onSend={handleSendMessage} disabled={loading} />
      </div>
    </div>
  );
}
