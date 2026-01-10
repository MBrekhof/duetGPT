import React, { useEffect, useRef } from 'react';
import type { Message } from '@/types';
import ReactMarkdown from 'react-markdown';
import { Bot, User } from 'lucide-react';

interface MessageListProps {
  messages: Message[];
}

export default function MessageList({ messages }: MessageListProps) {
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  return (
    <div className="flex-1 overflow-y-auto p-4 space-y-4">
      {messages.map((message) => (
        <div
          key={message.id}
          className={`flex gap-3 ${
            message.role === 'user' ? 'justify-end' : 'justify-start'
          }`}
        >
          {message.role === 'assistant' && (
            <div className="flex-shrink-0 w-8 h-8 rounded-full bg-blue-600 flex items-center justify-center">
              <Bot className="w-5 h-5 text-white" />
            </div>
          )}
          <div
            className={`max-w-[70%] rounded-lg p-4 ${
              message.role === 'user'
                ? 'bg-blue-600 text-white'
                : 'bg-gray-100 dark:bg-gray-800 text-gray-900 dark:text-gray-100'
            }`}
          >
            {message.role === 'assistant' ? (
              <div className="prose prose-sm dark:prose-invert max-w-none">
                <ReactMarkdown>{message.content}</ReactMarkdown>
              </div>
            ) : (
              <p className="whitespace-pre-wrap">{message.content}</p>
            )}
            {message.tokens && (
              <div className="text-xs mt-2 opacity-70">
                Tokens: {message.tokens}
              </div>
            )}
          </div>
          {message.role === 'user' && (
            <div className="flex-shrink-0 w-8 h-8 rounded-full bg-gray-300 dark:bg-gray-700 flex items-center justify-center">
              <User className="w-5 h-5 text-gray-700 dark:text-gray-300" />
            </div>
          )}
        </div>
      ))}
      <div ref={messagesEndRef} />
    </div>
  );
}
