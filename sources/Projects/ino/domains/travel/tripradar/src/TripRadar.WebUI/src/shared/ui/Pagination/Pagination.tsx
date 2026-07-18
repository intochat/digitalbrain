import { cn } from 'shared/lib/utils';

interface PaginationProps {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  className?: string;
}

const buttonBase = cn(
  'inline-flex items-center justify-center rounded-lg px-3 py-1.5 font-medium text-sm',
  'transition-colors duration-150 focus:outline-none focus:ring-2 focus:ring-content/10',
  'disabled:opacity-50 disabled:cursor-not-allowed'
);

export const Pagination = ({ currentPage, totalPages, onPageChange, className }: PaginationProps) => {
  if (totalPages <= 1) return null;

  const pages = Array.from({ length: totalPages }, (_, i) => i + 1);

  return (
    <nav aria-label="Pagination" className={cn('flex items-center gap-1', className)}>
      <button
        type="button"
        disabled={currentPage <= 1}
        onClick={() => onPageChange(currentPage - 1)}
        className={cn(
          buttonBase,
          'text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark'
        )}
        aria-label="Previous page"
      >
        ←
      </button>

      {pages.map(page => (
        <button
          key={page}
          type="button"
          onClick={() => onPageChange(page)}
          aria-current={page === currentPage ? 'page' : undefined}
          className={cn(
            buttonBase,
            page === currentPage
              ? 'bg-surface-accent dark:bg-surface-accent-dark text-content dark:text-content-dark'
              : 'text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark'
          )}
        >
          {page}
        </button>
      ))}

      <button
        type="button"
        disabled={currentPage >= totalPages}
        onClick={() => onPageChange(currentPage + 1)}
        className={cn(
          buttonBase,
          'text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark'
        )}
        aria-label="Next page"
      >
        →
      </button>
    </nav>
  );
};
