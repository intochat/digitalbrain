import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { handleGoogleSignUp, processGoogleRedirectSignIn } from '../lib/oauth';
import { OAuthButtons } from './OAuthButtons';

vi.mock('../lib/oauth', () => ({
  handleGoogleSignUp: vi.fn(),
  processGoogleRedirectSignIn: vi.fn().mockResolvedValue(null),
}));

describe('OAuthButtons Functionality and Accessibility', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(processGoogleRedirectSignIn).mockResolvedValue(null);
  });

  it('renders only Google by default', () => {
    render(<OAuthButtons />);

    expect(screen.getByRole('button', { name: /continue with google/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /continue with telegram/i })).not.toBeInTheDocument();
  });

  it('renders Telegram button when enabled', () => {
    render(<OAuthButtons providers={['google', 'telegram']} />);

    expect(screen.getByRole('button', { name: /continue with google/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /continue with telegram/i })).toBeInTheDocument();
  });

  it('calls handleGoogleSignUp when Google button is clicked', async () => {
    vi.mocked(handleGoogleSignUp).mockResolvedValue({ success: true });

    render(<OAuthButtons />);

    fireEvent.click(screen.getByRole('button', { name: /continue with google/i }));

    await waitFor(() => expect(handleGoogleSignUp).toHaveBeenCalledTimes(1));
  });

  it('calls onTelegramClick when Telegram button is clicked', async () => {
    const onTelegramClick = vi.fn();

    render(<OAuthButtons providers={['telegram']} onTelegramClick={onTelegramClick} />);

    fireEvent.click(screen.getByRole('button', { name: /continue with telegram/i }));

    await waitFor(() => expect(onTelegramClick).toHaveBeenCalledTimes(1));
  });

  it('renders Google OAuth errors with red styling', async () => {
    vi.mocked(handleGoogleSignUp).mockResolvedValue({ success: false, error: 'OAuth failed' });

    render(<OAuthButtons />);

    fireEvent.click(screen.getByRole('button', { name: /continue with google/i }));

    const oauthErrorAlert = await screen.findByRole('alert');
    expect(oauthErrorAlert).toHaveClass('border-red-200', 'bg-red-50', 'dark:border-red-900/70', 'dark:bg-red-950/30');
    expect(screen.getByText('Google sign-in failed')).toHaveClass('text-red-800', 'dark:text-red-300');
    expect(screen.getByText('OAuth failed')).toHaveClass('text-red-700', 'dark:text-red-300');
  });

  it('processes Google redirect result on mount only when Google provider is enabled', async () => {
    render(<OAuthButtons providers={['telegram']} />);

    await waitFor(() => expect(processGoogleRedirectSignIn).not.toHaveBeenCalled());

    render(<OAuthButtons providers={['google']} />);

    await waitFor(() => expect(processGoogleRedirectSignIn).toHaveBeenCalledTimes(1));
  });

  it('has proper semantic structure and accessibility', () => {
    render(<OAuthButtons providers={['google', 'telegram']} />);

    const googleButton = screen.getByRole('button', { name: /continue with google/i });
    const telegramButton = screen.getByRole('button', { name: /continue with telegram/i });

    expect(googleButton).toHaveClass('min-h-[48px]');
    expect(telegramButton).toHaveClass('min-h-[48px]');
    expect(googleButton.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    expect(telegramButton.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    expect(googleButton).toBeEnabled();
    expect(telegramButton).toBeEnabled();
  });
});
