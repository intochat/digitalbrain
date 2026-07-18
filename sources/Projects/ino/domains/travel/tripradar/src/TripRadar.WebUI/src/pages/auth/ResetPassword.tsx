import { useMemo, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { CheckCircle, Eye, EyeOff, Lock } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useResetPasswordMutation } from 'entities/auth';
import { ROUTES } from 'shared/config/routes';

const resetPasswordSchema = z
  .object({
    newPassword: z.string().min(8, 'Password must be at least 8 characters'),
    confirmPassword: z.string(),
  })
  .refine(data => data.newPassword === data.confirmPassword, {
    message: 'Passwords do not match',
    path: ['confirmPassword'],
  });

type ResetPasswordFormData = z.infer<typeof resetPasswordSchema>;

export const ResetPassword = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const hashParams = useMemo(
    () => new URLSearchParams(location.hash.startsWith('#') ? location.hash.slice(1) : location.hash),
    [location.hash]
  );
  const token = searchParams.get('token') ?? hashParams.get('token');
  const username = searchParams.get('username') ?? hashParams.get('username') ?? '';
  const { showError } = useToast();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ResetPasswordFormData>({
    resolver: zodResolver(resetPasswordSchema),
    mode: 'onSubmit',
    defaultValues: { newPassword: '', confirmPassword: '' },
  });
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);

  const resetPasswordMutation = useResetPasswordMutation();

  if (!token) {
    return (
      <div className="relative flex-1 flex items-center justify-center p-4">
        <div className="absolute inset-0 bg-surface dark:bg-surface-dark" />
        <div className="relative z-10 text-center">
          <h2 className="text-xl font-semibold text-content dark:text-content-dark mb-2">{t('Invalid reset link')}</h2>
          <p className="text-content-secondary dark:text-content-secondary-dark mb-4">
            {t('This password reset link is invalid or has expired.')}
          </p>
          <Link to={ROUTES.FORGOT_PASSWORD} className="text-primary-600 hover:text-primary-700 touch-manipulation">
            {t('Request a new reset link')}
          </Link>
        </div>
      </div>
    );
  }

  const onSubmit = (data: ResetPasswordFormData) => {
    resetPasswordMutation.mutate(
      { token, newPassword: data.newPassword, username },
      {
        onSuccess: () => {
          setIsSuccess(true);
        },
        onError: error => {
          console.error('Reset password failed:', error);
          showError(t('Failed to reset password'), t('Please try again or request a new reset link.'));
        },
      }
    );
  };

  if (isSuccess) {
    return (
      <div className="relative flex-1 flex items-center justify-center p-4">
        <div className="absolute inset-0 bg-surface dark:bg-surface-dark" />

        <div className="relative z-10 w-full max-w-md">
          <div className="bg-surface dark:bg-surface-dark rounded-xl border border-outline dark:border-outline-dark p-6 text-center">
            <div className="w-16 h-16 bg-green-100 dark:bg-green-900 rounded-full flex items-center justify-center mx-auto mb-4">
              <CheckCircle className="h-8 w-8 text-green-600 dark:text-green-400" />
            </div>

            <h2 className="text-xl font-semibold text-content dark:text-content-dark mb-2">
              {t('Password reset successful')}
            </h2>

            <p className="text-content-secondary dark:text-content-secondary-dark mb-4">
              {t('Your password has been successfully reset. You can now log in with your new password.')}
            </p>

            <Link
              to={ROUTES.LOGIN}
              className="inline-flex items-center justify-center bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark px-6 py-3 min-h-[48px] rounded-xl font-medium transition-all duration-200 focus:outline-none focus:ring-2 focus:ring-content/10 focus:ring-offset-2 focus:ring-offset-surface dark:focus:ring-offset-surface-dark touch-manipulation"
            >
              {t('Go to login')}
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="relative flex-1 flex items-center justify-center p-4 md:p-8">
      <div className="absolute inset-0 bg-surface dark:bg-surface-dark" />

      <div className="relative z-10 w-full max-w-md">
        <div className="text-center mb-8">
          <h2 className="text-2xl md:text-3xl font-semibold text-content dark:text-content-dark mb-2">
            {t('Reset your password')}
          </h2>
          <p className="text-content-secondary dark:text-content-secondary-dark">
            {t('Enter your new password below')}
          </p>
        </div>

        <div className="bg-surface dark:bg-surface-dark rounded-xl border border-outline dark:border-outline-dark p-6">
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
            <div>
              <label
                htmlFor="newPassword"
                className="block text-sm font-medium text-content dark:text-content-dark mb-2"
              >
                {t('New Password')}
              </label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Lock className="h-5 w-5 text-content-muted" />
                </div>
                <input
                  id="newPassword"
                  type={showPassword ? 'text' : 'password'}
                  {...register('newPassword')}
                  className="block w-full pl-10 pr-10 py-3 min-h-[44px] border border-outline dark:border-outline-dark rounded-xl placeholder-content-muted focus:outline-none focus:ring-2 focus:ring-content/10 focus:border-primary-500 dark:focus:border-primary-400 bg-surface dark:bg-surface-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark text-content dark:text-content-dark transition-all duration-200 touch-manipulation"
                  placeholder={t('Enter new password')}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-2 top-1/2 transform -translate-y-1/2 p-2 min-h-[44px] min-w-[44px] flex items-center justify-center text-content-muted hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark active:scale-95 rounded-lg transition-all duration-200 focus:outline-none focus:ring-2 focus:ring-content/10 touch-manipulation"
                  aria-label={showPassword ? t('Hide password') : t('Show password')}
                >
                  {showPassword ? <EyeOff className="h-5 w-5" /> : <Eye className="h-5 w-5" />}
                </button>
              </div>
              {errors.newPassword && (
                <p className="mt-1 text-sm text-red-600 dark:text-red-400">{t(errors.newPassword.message!)}</p>
              )}
            </div>

            <div>
              <label
                htmlFor="confirmPassword"
                className="block text-sm font-medium text-content dark:text-content-dark mb-2"
              >
                {t('Confirm Password')}
              </label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Lock className="h-5 w-5 text-content-muted" />
                </div>
                <input
                  id="confirmPassword"
                  type={showConfirmPassword ? 'text' : 'password'}
                  {...register('confirmPassword')}
                  className="block w-full pl-10 pr-10 py-3 min-h-[44px] border border-outline dark:border-outline-dark rounded-xl placeholder-content-muted focus:outline-none focus:ring-2 focus:ring-content/10 focus:border-primary-500 dark:focus:border-primary-400 bg-surface dark:bg-surface-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark text-content dark:text-content-dark transition-all duration-200 touch-manipulation"
                  placeholder={t('Confirm new password')}
                />
                <button
                  type="button"
                  onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                  className="absolute right-2 top-1/2 transform -translate-y-1/2 p-2 min-h-[44px] min-w-[44px] flex items-center justify-center text-content-muted hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark active:scale-95 rounded-lg transition-all duration-200 focus:outline-none focus:ring-2 focus:ring-content/10 touch-manipulation"
                  aria-label={showConfirmPassword ? t('Hide password') : t('Show password')}
                >
                  {showConfirmPassword ? <EyeOff className="h-5 w-5" /> : <Eye className="h-5 w-5" />}
                </button>
              </div>
              {errors.confirmPassword && (
                <p className="mt-1 text-sm text-red-600 dark:text-red-400">{t(errors.confirmPassword.message!)}</p>
              )}
            </div>

            <button
              type="submit"
              disabled={resetPasswordMutation.isPending}
              className="w-full flex justify-center py-3 px-4 min-h-[48px] border border-transparent rounded-xl text-sm font-medium bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark focus:outline-none focus:ring-2 focus:ring-content/10 focus:ring-offset-2 focus:ring-offset-surface dark:focus:ring-offset-surface-dark disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-button dark:disabled:hover:bg-button-dark transition-all duration-200 touch-manipulation"
            >
              {resetPasswordMutation.isPending ? (
                <>
                  <div className="animate-spin h-4 w-4 border-2 border-white/30 border-t-white rounded-full mr-2" />
                  {t('Resetting...')}
                </>
              ) : (
                t('Reset password')
              )}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
};
