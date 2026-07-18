import { Loader2 } from 'lucide-react';

interface SwitchProps {
  checked: boolean;
  onChange: () => void;
  disabled?: boolean;
  loading?: boolean;
  'aria-label'?: string;
}

export const Switch = ({ checked, onChange, disabled = false, loading = false, ...rest }: SwitchProps) => {
  const isInactive = disabled || loading;

  const trackClass = checked ? 'bg-content dark:bg-content-dark' : 'bg-outline dark:bg-outline-dark';
  const thumbClass = checked ? 'translate-x-[22px]' : 'translate-x-[2px]';

  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={isInactive}
      onClick={onChange}
      onKeyDown={event => {
        if (event.key === ' ' || event.key === 'Enter') {
          event.preventDefault();
          onChange();
        }
      }}
      className={`relative inline-flex h-[22px] w-[40px] flex-shrink-0 touch-manipulation cursor-pointer items-center rounded-full transition-colors duration-200 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-outline dark:focus-visible:ring-outline-dark disabled:cursor-not-allowed disabled:opacity-40 ${trackClass}`}
      {...rest}
    >
      {loading ? (
        <span className="absolute inset-0 flex items-center justify-center">
          <Loader2 className="h-3 w-3 animate-spin text-white dark:text-content-dark" />
        </span>
      ) : (
        <span
          className={`pointer-events-none inline-block h-4 w-4 rounded-full bg-white dark:bg-surface-dark transition-transform duration-200 ${thumbClass}`}
        />
      )}
    </button>
  );
};
