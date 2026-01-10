'use client';

import { useEffect, useState } from 'react';
import { apiClient } from '@/lib/api-client';

export default function TestPage() {
  const [status, setStatus] = useState<string>('Testing connection...');
  const [error, setError] = useState<string>('');

  useEffect(() => {
    testConnection();
  }, []);

  const testConnection = async () => {
    try {
      // Test 1: Try to fetch models (doesn't require auth)
      setStatus('Testing models endpoint...');
      const models = await apiClient.getModels();
      setStatus(`✅ Models endpoint works! Found ${models.length} models.`);

      // Test 2: Try to fetch threads (requires auth)
      try {
        setStatus('Testing threads endpoint (requires auth)...');
        const threads = await apiClient.getThreads();
        setStatus(`✅ Full success! Backend connection works. Found ${threads.length} threads.`);
      } catch (authError: any) {
        if (authError.response?.status === 401) {
          setStatus('✅ CORS working! Backend connection successful. (401 = authentication required, which is expected)');
        } else {
          throw authError;
        }
      }
    } catch (err: any) {
      console.error('Connection test failed:', err);
      if (err.code === 'ERR_NETWORK' || err.message?.includes('Network Error')) {
        setError('❌ Network error - Backend might not be running or CORS not configured');
      } else if (err.message?.includes('CORS')) {
        setError('❌ CORS error - Backend needs CORS configuration');
      } else {
        setError(`❌ Error: ${err.message}`);
      }
      setStatus('Connection test failed');
    }
  };

  return (
    <div className="min-h-screen bg-gray-100 dark:bg-gray-900 p-8">
      <div className="max-w-2xl mx-auto bg-white dark:bg-gray-800 rounded-lg shadow-lg p-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100 mb-4">
          Backend Connection Test
        </h1>

        <div className="space-y-4">
          <div className="p-4 bg-blue-50 dark:bg-blue-900 rounded-lg">
            <h2 className="font-semibold text-blue-900 dark:text-blue-100 mb-2">
              Configuration:
            </h2>
            <p className="text-sm text-blue-800 dark:text-blue-200">
              Backend URL: {process.env.NEXT_PUBLIC_API_URL || 'https://localhost:44391'}
            </p>
          </div>

          <div className={`p-4 rounded-lg ${error ? 'bg-red-50 dark:bg-red-900' : 'bg-green-50 dark:bg-green-900'}`}>
            <h2 className="font-semibold mb-2">Status:</h2>
            <p className="text-sm">{status}</p>
            {error && <p className="text-sm mt-2 text-red-600 dark:text-red-300">{error}</p>}
          </div>

          <button
            onClick={testConnection}
            className="w-full bg-blue-600 text-white py-2 px-4 rounded-lg hover:bg-blue-700 transition-colors"
          >
            Test Again
          </button>

          <div className="mt-4 p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
            <h2 className="font-semibold text-gray-900 dark:text-gray-100 mb-2">
              Next Steps:
            </h2>
            <ul className="text-sm text-gray-700 dark:text-gray-300 list-disc list-inside space-y-1">
              <li>If you see a CORS error, restart the .NET backend</li>
              <li>If you see a network error, make sure the backend is running at https://localhost:44391</li>
              <li>If you see 401 (authentication required), CORS is working correctly!</li>
              <li>Go back to <a href="/" className="text-blue-600 hover:underline">home page</a> to test the chat interface</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
