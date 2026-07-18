import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Signup } from './Signup';

// Mock the registration mutation
const mockMutate = vi.fn();
vi.mock('entities/auth', () => ({
  useRegisterMutation: () => ({
    mutate: mockMutate,
    isPending: false,
    isError: false,
  }),
}));

// Define types for the mock
interface MockMutationOptions {
  onSuccess: () => void;
  onError?: (error: unknown) => void;
}

interface MockRegistrationData {
  email: string;
  password: string;
  hasDataStorageConsent: boolean;
}

// Mock navigation
const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

const renderWithProviders = (component: React.ReactElement) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{component}</BrowserRouter>
    </QueryClientProvider>
  );
};

describe('Signup Session Management', () => {
  beforeEach(() => {
    // Clear sessionStorage before each test
    sessionStorage.clear();
    mockMutate.mockClear();
    mockNavigate.mockClear();
  });

  it('should store email in sessionStorage on successful registration', async () => {
    renderWithProviders(<Signup />);

    // Fill out the form
    const emailInput = screen.getByPlaceholderText('Enter your email');
    const passwordInput = screen.getByPlaceholderText('Create a password');
    const consentCheckbox = screen.getByRole('checkbox');
    const submitButton = screen.getByRole('button', { name: /get started/i });

    fireEvent.change(emailInput, { target: { value: 'test@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'TestPass123!' } });
    fireEvent.click(consentCheckbox);

    // Mock successful registration
    mockMutate.mockImplementation((_data: MockRegistrationData, { onSuccess }: MockMutationOptions) => {
      onSuccess();
    });

    fireEvent.click(submitButton);

    await waitFor(() => {
      // Verify that email was stored in sessionStorage
      expect(sessionStorage.getItem('registration_email')).toBe('test@example.com');
    });
  });

  it('should use sessionStorage not localStorage for registration email', async () => {
    renderWithProviders(<Signup />);

    // Fill out the form
    const emailInput = screen.getByPlaceholderText('Enter your email');
    const passwordInput = screen.getByPlaceholderText('Create a password');
    const consentCheckbox = screen.getByRole('checkbox');
    const submitButton = screen.getByRole('button', { name: /get started/i });

    fireEvent.change(emailInput, { target: { value: 'test@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'TestPass123!' } });
    fireEvent.click(consentCheckbox);

    // Mock successful registration
    mockMutate.mockImplementation((_data: MockRegistrationData, { onSuccess }: MockMutationOptions) => {
      onSuccess();
    });

    fireEvent.click(submitButton);

    await waitFor(() => {
      // Verify sessionStorage is used, not localStorage
      expect(sessionStorage.getItem('registration_email')).toBe('test@example.com');
      expect(localStorage.getItem('registration_email')).toBeNull();
    });
  });
});
