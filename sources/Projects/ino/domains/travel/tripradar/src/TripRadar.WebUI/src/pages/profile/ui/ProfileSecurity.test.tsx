import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useNavigate } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useToast } from 'app/providers/ToastProvider';
import { useSubscriptionQuery } from 'entities/payment/api';
import { usePrivacyModeQuery, useUpdatePrivacyModeMutation } from 'entities/preferences/api';
import {
  useChangePasswordMutation,
  useDeleteAccountMutation,
  useProfileQuery,
  useUpdateProfileMutation,
} from 'entities/user/api';
import { useAuthStore } from 'shared/store/auth';
import { ProfileSecurity } from './ProfileSecurity';

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: vi.fn(),
  };
});
vi.mock('shared/store/auth');
vi.mock('entities/user/api');
vi.mock('entities/payment/api');
vi.mock('entities/preferences/api');
vi.mock('app/providers/ToastProvider');

vi.mock('./ProfileLayout', () => ({
  ProfileLayout: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

const mockUseAuthStore = vi.mocked(useAuthStore);
const mockUseProfileQuery = vi.mocked(useProfileQuery);
const mockUseChangePasswordMutation = vi.mocked(useChangePasswordMutation);
const mockUseDeleteAccountMutation = vi.mocked(useDeleteAccountMutation);
const mockUseToast = vi.mocked(useToast);
const mockUseNavigate = vi.mocked(useNavigate);
const mockUseSubscriptionQuery = vi.mocked(useSubscriptionQuery);
const mockUsePrivacyModeQuery = vi.mocked(usePrivacyModeQuery);
const mockUseUpdatePrivacyModeMutation = vi.mocked(useUpdatePrivacyModeMutation);
const mockUseUpdateProfileMutation = vi.mocked(useUpdateProfileMutation);

describe('ProfileSecurity', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockUseAuthStore.mockReturnValue({
      user: { username: 'testuser' },
      isAuthenticated: true,
      isLoading: false,
      login: vi.fn(),
      logout: vi.fn(),
      updateUser: vi.fn(),
      initializeAuth: vi.fn(),
    });

    mockUseProfileQuery.mockReturnValue({
      data: {
        username: 'testuser',
        email: 'user@example.com',
        isEmailConfirmed: false,
        firstName: null,
        lastName: null,
        phoneNumber: null,
        googleId: null,
        timezoneId: 1,
        profilePictureUrl: null,
        languageCode: 'en',
        languageName: 'English',
        countryCode: 'us',
        countryName: 'United States',
        allowsMarketingEmails: true,
        isActive: true,
        tierName: 'basic',
        createdOn: '2026-02-10T10:00:00Z',
        updatedOn: '2026-02-10T10:00:00Z',
      },
      isLoading: false,
    } as unknown as ReturnType<typeof useProfileQuery>);

    mockUseChangePasswordMutation.mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
      isPending: false,
    } as unknown as ReturnType<typeof useChangePasswordMutation>);

    mockUseDeleteAccountMutation.mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
      isPending: false,
    } as unknown as ReturnType<typeof useDeleteAccountMutation>);

    mockUseUpdateProfileMutation.mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateProfileMutation>);

    mockUseSubscriptionQuery.mockReturnValue({
      data: null,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useSubscriptionQuery>);

    mockUsePrivacyModeQuery.mockReturnValue({
      data: { enabled: false },
      isLoading: false,
    } as unknown as ReturnType<typeof usePrivacyModeQuery>);

    mockUseUpdatePrivacyModeMutation.mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
      isPending: false,
    } as unknown as ReturnType<typeof useUpdatePrivacyModeMutation>);

    mockUseToast.mockReturnValue({
      toasts: [],
      showSuccess: vi.fn(),
      showError: vi.fn(),
      showInfo: vi.fn(),
      addToast: vi.fn(),
      removeToast: vi.fn(),
    });

    mockUseNavigate.mockReturnValue(vi.fn());
  });

  it('does not submit password change when confirmation password mismatches', async () => {
    const changePasswordMutation = vi.fn().mockResolvedValue({});
    mockUseChangePasswordMutation.mockReturnValue({
      mutateAsync: changePasswordMutation,
      isPending: false,
    } as unknown as ReturnType<typeof useChangePasswordMutation>);

    render(<ProfileSecurity />);

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'OldPass#1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'NewPass#2' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'WrongPass#2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('New password and confirmation do not match.')).toBeInTheDocument();
    expect(changePasswordMutation).not.toHaveBeenCalled();
  });

  it('submits change password payload when form is valid', async () => {
    const changePasswordMutation = vi.fn().mockResolvedValue({});
    mockUseChangePasswordMutation.mockReturnValue({
      mutateAsync: changePasswordMutation,
      isPending: false,
    } as unknown as ReturnType<typeof useChangePasswordMutation>);

    render(<ProfileSecurity />);

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'OldPass#1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'NewPass#2' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'NewPass#2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Change password' }));

    await waitFor(() => {
      expect(changePasswordMutation).toHaveBeenCalledWith({
        currentPassword: 'OldPass#1',
        newPassword: 'NewPass#2',
      });
    });
  });

  it('deletes account after username confirmation', async () => {
    const deleteAccountMutation = vi.fn().mockResolvedValue({});
    const logout = vi.fn();
    const navigate = vi.fn();

    mockUseAuthStore.mockReturnValue({
      user: { username: 'testuser' },
      isAuthenticated: true,
      isLoading: false,
      login: vi.fn(),
      logout,
      updateUser: vi.fn(),
      initializeAuth: vi.fn(),
    });

    mockUseDeleteAccountMutation.mockReturnValue({
      mutateAsync: deleteAccountMutation,
      isPending: false,
    } as unknown as ReturnType<typeof useDeleteAccountMutation>);

    mockUseNavigate.mockReturnValue(navigate);

    render(<ProfileSecurity />);

    fireEvent.click(screen.getByRole('button', { name: 'Delete account' }));
    fireEvent.change(screen.getByLabelText('Type your username to confirm'), {
      target: { value: 'testuser' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Delete account permanently' }));

    await waitFor(() => {
      expect(deleteAccountMutation).toHaveBeenCalledTimes(1);
    });

    expect(logout).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith('/', { replace: true });
  });
});
