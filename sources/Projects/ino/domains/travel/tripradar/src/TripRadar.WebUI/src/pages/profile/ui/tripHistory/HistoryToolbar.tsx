import { useMemo } from 'react';
import { RefreshCw } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { Dropdown } from 'shared/ui';
import type { DropdownOption } from 'shared/ui';
import { HISTORY_PAGE_SIZES } from './tripHistoryUtils';

interface HistoryToolbarProps {
  pageSize: number;
  isFetching: boolean;
  onPageSizeChange: (size: number) => void;
  onRefresh: () => void;
}

export const HistoryToolbar = ({ pageSize, isFetching, onPageSizeChange, onRefresh }: HistoryToolbarProps) => {
  const { t } = useFrontendLanguage();

  const pageSizeOptions: DropdownOption<number>[] = useMemo(
    () => HISTORY_PAGE_SIZES.map(size => ({ value: size, label: `${size} / ${t('page')}` })),
    [t]
  );

  return (
    <div className="flex items-center gap-3">
      <div className="w-[120px]">
        <Dropdown
          value={pageSize}
          options={pageSizeOptions}
          onChange={onPageSizeChange}
          aria-label={t('History page size')}
          className="!py-1 !px-2 !text-[11px]"
        />
      </div>
      <button
        type="button"
        onClick={onRefresh}
        className="p-1.5 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
        aria-label={t('Refresh')}
      >
        <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
      </button>
    </div>
  );
};
