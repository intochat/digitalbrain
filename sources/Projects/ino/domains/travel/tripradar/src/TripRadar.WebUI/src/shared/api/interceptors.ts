import { env } from 'shared/config';

export interface ApiError {
  code: string;
  message: string;
}

export interface RequestBehavior {
  skipUnauthorizedRedirect?: boolean;
}

export class ApiClient {
  private baseURL: string;
  private apiKey: string;
  private static readonly tokenRefreshedErrorMessage = 'TOKEN_REFRESHED';

  constructor() {
    this.baseURL = env.API_BASE_URL;
    this.apiKey = env.API_KEY;
  }

  async request<T>(
    endpoint: string,
    options: RequestInit = {},
    retryCount = 0,
    behavior?: RequestBehavior
  ): Promise<T> {
    const url = import.meta.env.DEV ? endpoint : this.baseURL + endpoint;
    const headers = new Headers(options.headers ?? {});

    if (!headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }

    headers.set('X-API-Key', this.apiKey);
    headers.set('X-ClientId', '127.0.0.1');

    const csrfToken = this.getCsrfToken();
    if (this.requiresCsrfHeader(options.method) && csrfToken && !headers.has('X-CSRF-TOKEN')) {
      headers.set('X-CSRF-TOKEN', csrfToken);
    }

    const requestData: RequestInit = {
      ...options,
      headers,
      credentials: 'include',
    };

    const response = await fetch(url, requestData);

    if (!response.ok) {
      try {
        await this.handleError(response, retryCount, behavior);
      } catch (error) {
        if (error instanceof Error && error.message === ApiClient.tokenRefreshedErrorMessage && retryCount === 0) {
          return this.request<T>(endpoint, options, retryCount + 1, behavior);
        }

        throw error;
      }
    }

    if (response.status === 204) {
      return undefined as T;
    }

    const responseBody = await response.text();
    if (!responseBody) {
      return undefined as T;
    }

    try {
      return JSON.parse(responseBody) as T;
    } catch {
      return responseBody as T;
    }
  }

  async get<T>(endpoint: string, behavior?: RequestBehavior): Promise<T> {
    return this.request<T>(endpoint, { method: 'GET' }, 0, behavior);
  }

  async post<T, D = object>(endpoint: string, data?: D): Promise<T> {
    return this.request<T>(endpoint, {
      method: 'POST',
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  async put<T, D = object>(endpoint: string, data?: D): Promise<T> {
    return this.request<T>(endpoint, {
      method: 'PUT',
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  async patch<T, D = object>(endpoint: string, data?: D): Promise<T> {
    return this.request<T>(endpoint, {
      method: 'PATCH',
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  async delete<T, D = object>(endpoint: string, data?: D): Promise<T> {
    return this.request<T>(endpoint, {
      method: 'DELETE',
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  private getCsrfToken(): string | null {
    const cookie = document.cookie.split('; ').find(item => item.startsWith('XSRF-TOKEN='));

    if (!cookie) {
      return null;
    }

    return decodeURIComponent(cookie.substring('XSRF-TOKEN='.length));
  }

  private requiresCsrfHeader(method?: string): boolean {
    if (!method) {
      return false;
    }

    return !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method.toUpperCase());
  }

  private async refreshTokens(): Promise<boolean> {
    const url = import.meta.env.DEV ? '/api/v1/tokens/refresh-tokens' : this.baseURL + '/api/v1/tokens/refresh-tokens';

    const headers = new Headers({
      'Content-Type': 'application/json',
      'X-API-Key': this.apiKey,
      'X-ClientId': '127.0.0.1',
    });

    const csrfToken = this.getCsrfToken();
    if (csrfToken) {
      headers.set('X-CSRF-TOKEN', csrfToken);
    }

    const response = await fetch(url, {
      method: 'POST',
      headers,
      credentials: 'include',
      body: JSON.stringify({}),
    });

    return response.ok;
  }

  private async handleError(response: Response, retryCount: number, behavior?: RequestBehavior): Promise<never> {
    if (response.status === 401) {
      if (behavior?.skipUnauthorizedRedirect) {
        throw new Error('Unauthorized');
      }

      if (retryCount === 0) {
        try {
          const refreshed = await this.refreshTokens();
          if (refreshed) {
            throw new Error(ApiClient.tokenRefreshedErrorMessage);
          }
        } catch (error) {
          if (error instanceof Error && error.message === ApiClient.tokenRefreshedErrorMessage) {
            throw error;
          }
        }
      }

      window.location.href = '/signin';
      throw new Error('Unauthorized - redirecting to login');
    }

    let errorData: ApiError & {
      type?: string;
      error?: string;
      errorCode?: string;
      email?: string;
      reason?: string;
      errorReason?: string;
      detail?: string;
      Type?: string;
      Error?: string;
      ErrorCode?: string;
      Email?: string;
      Reason?: string;
      ErrorReason?: string;
      Message?: string;
      Detail?: string;
      Code?: string;
    };
    try {
      errorData = await response.json();
    } catch {
      errorData = {
        code: 'UNKNOWN_ERROR',
        message: 'HTTP ' + response.status + ': ' + response.statusText,
      };
    }

    const normalizedErrorCode =
      errorData.errorCode ||
      errorData.ErrorCode ||
      errorData.code ||
      errorData.Code ||
      errorData.type ||
      errorData.Type ||
      errorData.error ||
      errorData.Error;

    const isTelegramRequired =
      (response.status === 400 || response.status === 403) && normalizedErrorCode === 'TELEGRAM_REQUIRED';

    if (isTelegramRequired) {
      const error = new Error(
        errorData.detail ||
          errorData.Detail ||
          errorData.message ||
          errorData.Message ||
          'Telegram account linking required'
      ) as Error & {
        email?: string;
        isTelegramRequired?: boolean;
        statusCode?: number;
      };
      error.email = errorData.email || errorData.Email;
      error.isTelegramRequired = true;
      error.statusCode = response.status;
      throw error;
    }

    const error = new Error(
      errorData.detail ||
        errorData.Detail ||
        errorData.reason ||
        errorData.Reason ||
        errorData.errorReason ||
        errorData.ErrorReason ||
        errorData.message ||
        errorData.Message ||
        'API request failed'
    ) as Error & {
      response?: {
        data?: unknown;
        status?: number;
      };
      code?: string;
    };

    error.response = {
      data: errorData,
      status: response.status,
    };
    error.code = normalizedErrorCode;

    throw error;
  }
}

export const apiClient = new ApiClient();
