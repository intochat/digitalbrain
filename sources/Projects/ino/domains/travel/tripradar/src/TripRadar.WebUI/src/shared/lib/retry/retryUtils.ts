export interface RetryOptions {
  maxAttempts?: number;
  baseDelayMs?: number;
  maxDelayMs?: number;
  backoffFactor?: number;
  shouldRetry?: (error: unknown) => boolean;
}

export interface RetryState {
  attempt: number;
  isRetrying: boolean;
  lastError?: unknown;
}

const DEFAULT_OPTIONS: Required<RetryOptions> = {
  maxAttempts: 3,
  baseDelayMs: 1000,
  maxDelayMs: 10000,
  backoffFactor: 2,
  shouldRetry: (error: unknown) => {
    // Don't retry on client errors (4xx), but retry on server errors (5xx) and network errors
    if (error && typeof error === 'object' && 'response' in error) {
      const apiError = error as { response?: { status?: number } };
      const status = apiError.response?.status;
      if (status && status >= 400 && status < 500) {
        // Don't retry on 404 (not found) or 401 (unauthorized)
        return status !== 404 && status !== 401;
      }
    }
    return true;
  },
};

export const calculateDelay = (attempt: number, options: RetryOptions = {}): number => {
  const { baseDelayMs, maxDelayMs, backoffFactor } = { ...DEFAULT_OPTIONS, ...options };

  const delay = baseDelayMs * Math.pow(backoffFactor, attempt - 1);
  return Math.min(delay, maxDelayMs);
};

export const sleep = (ms: number): Promise<void> => {
  return new Promise(resolve => setTimeout(resolve, ms));
};

export const withRetry = async <T>(operation: () => Promise<T>, options: RetryOptions = {}): Promise<T> => {
  const config = { ...DEFAULT_OPTIONS, ...options };
  let lastError: unknown;

  for (let attempt = 1; attempt <= config.maxAttempts; attempt++) {
    try {
      return await operation();
    } catch (error) {
      lastError = error;

      // Don't retry if we've reached max attempts or if error shouldn't be retried
      if (attempt >= config.maxAttempts || !config.shouldRetry(error)) {
        throw error;
      }

      // Wait before retrying
      const delay = calculateDelay(attempt, options);
      await sleep(delay);
    }
  }

  throw lastError;
};

export class RetryManager {
  private retryState: RetryState = {
    attempt: 0,
    isRetrying: false,
  };

  private options: Required<RetryOptions>;

  constructor(options: RetryOptions = {}) {
    this.options = { ...DEFAULT_OPTIONS, ...options };
  }

  async execute<T>(operation: () => Promise<T>): Promise<T> {
    this.retryState = {
      attempt: 0,
      isRetrying: false,
      lastError: undefined,
    };

    return withRetry(operation, this.options);
  }

  async executeWithState<T>(operation: () => Promise<T>, onStateChange?: (state: RetryState) => void): Promise<T> {
    this.retryState = {
      attempt: 0,
      isRetrying: false,
      lastError: undefined,
    };

    let lastError: unknown;

    for (let attempt = 1; attempt <= this.options.maxAttempts; attempt++) {
      this.retryState = {
        attempt,
        isRetrying: attempt > 1,
        lastError,
      };

      onStateChange?.(this.retryState);

      try {
        const result = await operation();

        // Success - reset state
        this.retryState = {
          attempt: 0,
          isRetrying: false,
          lastError: undefined,
        };
        onStateChange?.(this.retryState);

        return result;
      } catch (error) {
        lastError = error;
        this.retryState.lastError = error;

        // Don't retry if we've reached max attempts or if error shouldn't be retried
        if (attempt >= this.options.maxAttempts || !this.options.shouldRetry(error)) {
          this.retryState.isRetrying = false;
          onStateChange?.(this.retryState);
          throw error;
        }

        // Wait before retrying
        const delay = calculateDelay(attempt, this.options);
        await sleep(delay);
      }
    }

    throw lastError;
  }

  getState(): RetryState {
    return { ...this.retryState };
  }

  reset(): void {
    this.retryState = {
      attempt: 0,
      isRetrying: false,
      lastError: undefined,
    };
  }
}

// Error classification utilities
export const isNetworkError = (error: unknown): boolean => {
  if (error && typeof error === 'object') {
    const err = error as { code?: string; message?: string };
    return err.code === 'NETWORK_ERROR' || err.message?.includes('Network Error') || err.message?.includes('fetch');
  }
  return false;
};

export const isServerError = (error: unknown): boolean => {
  if (error && typeof error === 'object' && 'response' in error) {
    const apiError = error as { response?: { status?: number } };
    const status = apiError.response?.status;
    return status ? status >= 500 : false;
  }
  return false;
};

export const isClientError = (error: unknown): boolean => {
  if (error && typeof error === 'object' && 'response' in error) {
    const apiError = error as { response?: { status?: number } };
    const status = apiError.response?.status;
    return status ? status >= 400 && status < 500 : false;
  }
  return false;
};

export const getErrorMessage = (error: unknown): string => {
  if (error && typeof error === 'object') {
    if ('message' in error && typeof error.message === 'string') {
      return error.message;
    }
    if ('response' in error) {
      const apiError = error as { response?: { data?: { message?: string }; status?: number } };
      if (apiError.response?.data?.message) {
        return apiError.response.data.message;
      }
      if (apiError.response?.status) {
        return `Request failed with status ${apiError.response.status}`;
      }
    }
  }
  return 'An unexpected error occurred';
};
