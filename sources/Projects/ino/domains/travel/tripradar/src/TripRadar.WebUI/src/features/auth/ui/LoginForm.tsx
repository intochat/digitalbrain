import { useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { useFrontendLanguage } from 'app/providers';
import { Button, Input } from 'shared/ui';

export interface LoginFormData {
  usernameOrEmail: string;
  password: string;
}

interface LoginFormProps {
  isPending: boolean;
  showSuccessState: boolean;
  defaultEmail: string;
  inlineError?: string | null;
  onSubmit: (data: LoginFormData) => void;
}

export const LoginForm = ({ isPending, showSuccessState, defaultEmail, inlineError, onSubmit }: LoginFormProps) => {
  const { t } = useFrontendLanguage();
  const [showPassword, setShowPassword] = useState(false);
  const disabled = isPending || showSuccessState;

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginFormData>({
    mode: 'onSubmit',
    defaultValues: { usernameOrEmail: defaultEmail, password: '' },
  });

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate aria-busy={isPending}>
      <div>
        <label htmlFor="email-input" className="block text-sm font-medium text-content dark:text-content-dark mb-1">
          {t('Email address')}
        </label>
        <Input
          {...register('usernameOrEmail', {
            required: t('Email is required'),
            pattern: { value: /^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$/i, message: t('Invalid email address') },
          })}
          id="email-input"
          type="email"
          inputMode="email"
          autoComplete="email"
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          error={!!errors.usernameOrEmail}
          disabled={disabled}
          placeholder={t('Enter your email')}
          aria-describedby={errors.usernameOrEmail ? 'email-error' : undefined}
          aria-invalid={errors.usernameOrEmail ? 'true' : 'false'}
          aria-required="true"
        />
        {errors.usernameOrEmail && (
          <p id="email-error" className="mt-1 text-xs text-red-500 dark:text-red-400" role="alert" aria-live="polite">
            {errors.usernameOrEmail.message}
          </p>
        )}
      </div>

      <div>
        <label htmlFor="password-input" className="block text-sm font-medium text-content dark:text-content-dark mb-1">
          {t('Password')}
        </label>
        <div className="relative">
          <Input
            {...register('password', { required: t('Password is required') })}
            id="password-input"
            type={showPassword ? 'text' : 'password'}
            autoComplete="current-password"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            className="pr-10"
            error={!!errors.password}
            disabled={disabled}
            placeholder={t('Enter your password')}
            aria-describedby={errors.password ? 'password-error' : undefined}
            aria-invalid={errors.password ? 'true' : 'false'}
            aria-required="true"
          />
          <button
            type="button"
            onClick={() => setShowPassword(p => !p)}
            disabled={disabled}
            className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-content-muted hover:text-content dark:hover:text-content-dark rounded transition-colors focus:outline-none focus:ring-2 focus:ring-content/10"
            aria-label={showPassword ? t('Hide password') : t('Show password')}
            tabIndex={disabled ? -1 : 0}
          >
            {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
          </button>
        </div>
        {errors.password && (
          <p
            id="password-error"
            className="mt-1 text-xs text-red-500 dark:text-red-400"
            role="alert"
            aria-live="polite"
          >
            {errors.password.message}
          </p>
        )}
        {!errors.password && inlineError && (
          <p className="mt-1.5 text-xs text-red-600 dark:text-red-400" role="alert" aria-live="polite">
            {inlineError}
          </p>
        )}
      </div>

      <Button
        type="submit"
        disabled={disabled}
        isLoading={isPending}
        className={`w-full ${showSuccessState ? 'bg-primary-500 dark:bg-primary-600 cursor-default' : ''}`}
        aria-disabled={disabled}
      >
        {showSuccessState ? t('Welcome back!') : isPending ? t('Signing in...') : t('Sign in')}
      </Button>
    </form>
  );
};
