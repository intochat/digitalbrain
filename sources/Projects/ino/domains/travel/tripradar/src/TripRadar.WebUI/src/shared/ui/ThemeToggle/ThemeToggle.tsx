import { Sun, Moon } from 'lucide-react';
import { useTheme } from 'app/providers/ThemeContext';
import { THEME } from 'shared/config/constants';
import { cn } from 'shared/lib/utils';

interface ThemeToggleProps {
  className?: string;
}

export const ThemeToggle = ({ className }: ThemeToggleProps) => {
  const { theme, toggleTheme } = useTheme();

  return (
    <button
      onClick={toggleTheme}
      className={cn(
        // Mobile-first base styles with clean text-only styling
        'p-2 transition-colors duration-200 flex items-center justify-center',
        'min-w-11 min-h-11', // Mobile-first touch targets
        'text-content-secondary dark:text-content-secondary-dark',
        'hover:text-content dark:hover:text-content-dark',
        'focus:outline-none focus:text-content dark:focus:text-content-dark',
        'touch-manipulation',
        // Progressive enhancement for larger screens
        'md:min-w-10 md:min-h-10',
        className
      )}
      aria-label={`Switch to ${theme === THEME.LIGHT ? 'dark' : 'light'} mode`}
    >
      {theme === THEME.LIGHT && (
        <Sun
          className={cn(
            // Mobile-first icon sizing
            'h-5 w-5',
            // Progressive enhancement
            'md:h-5 md:w-5'
          )}
        />
      )}
      {theme === THEME.DARK && (
        <Moon
          className={cn(
            // Mobile-first icon sizing
            'h-5 w-5',
            // Progressive enhancement
            'md:h-5 md:w-5'
          )}
        />
      )}
    </button>
  );
};
