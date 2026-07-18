import type { ButtonHTMLAttributes } from 'react';
import { forwardRef } from 'react';
import { cn } from 'shared/lib/utils';

export type ButtonVariant = 'primary' | 'secondary' | 'destructive' | 'ghost';
export type ButtonSize = 'sm' | 'md' | 'lg';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  isLoading?: boolean;
}

const variants: Record<ButtonVariant, string> = {
  primary: [
    'bg-button dark:bg-button-dark',
    'text-button-text dark:text-button-text-dark',
    'hover:bg-button-hover dark:hover:bg-button-hover-dark',
  ].join(' '),
  secondary: [
    'border border-outline dark:border-outline-dark',
    'text-content dark:text-content-dark',
    'bg-surface dark:bg-surface-dark',
    'hover:bg-surface-accent dark:hover:bg-surface-accent-dark',
  ].join(' '),
  destructive: ['bg-red-600 dark:bg-red-500', 'text-white', 'hover:bg-red-700 dark:hover:bg-red-600'].join(' '),
  ghost: [
    'text-content-secondary dark:text-content-secondary-dark',
    'hover:text-content dark:hover:text-content-dark',
    'hover:bg-surface-accent dark:hover:bg-surface-accent-dark',
  ].join(' '),
};

const sizes: Record<ButtonSize, string> = {
  sm: 'px-3 py-1.5 text-xs',
  md: 'px-4 py-2.5 text-sm',
  lg: 'px-5 py-3 text-sm',
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'primary', size = 'md', isLoading = false, disabled, children, ...props }, ref) => (
    <button
      ref={ref}
      className={cn(
        'inline-flex items-center justify-center font-medium rounded-lg',
        'transition-colors duration-150',
        'focus:outline-none focus:ring-2 focus:ring-content/10',
        'disabled:opacity-50 disabled:cursor-not-allowed',
        variants[variant],
        sizes[size],
        isLoading && 'cursor-wait',
        className
      )}
      disabled={disabled || isLoading}
      {...props}
    >
      {children}
    </button>
  )
);

Button.displayName = 'Button';
