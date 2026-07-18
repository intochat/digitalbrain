import type { InputHTMLAttributes } from 'react';
import { forwardRef } from 'react';
import { cn } from 'shared/lib/utils';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  error?: boolean;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(({ className, error, ...props }, ref) => (
  <input
    ref={ref}
    className={cn(
      'w-full rounded-lg border bg-surface dark:bg-surface-dark px-3 py-2.5 text-sm',
      'text-content dark:text-content-dark placeholder-content-muted',
      'transition-colors duration-150 disabled:opacity-50 disabled:cursor-not-allowed',
      'focus:outline-none',
      error
        ? 'border-red-500 dark:border-red-400'
        : 'border-outline dark:border-outline-dark focus:border-content/40 dark:focus:border-content-dark/40',
      className
    )}
    {...props}
  />
));

Input.displayName = 'Input';
