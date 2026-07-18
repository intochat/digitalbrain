import { Button } from 'shared/ui';
import type { FilterTabCounts, FilterTabValue } from '../utils';

interface FilterTabsProps {
  activeTab: FilterTabValue;
  onTabChange: (tab: FilterTabValue) => void;
  counts: FilterTabCounts;
  t: (key: string, params?: Record<string, string | number>) => string;
}

const TABS: { value: FilterTabValue; labelKey: string }[] = [
  { value: 'all', labelKey: 'All' },
  { value: 'flights', labelKey: 'Flights' },
  { value: 'hotels', labelKey: 'Hotels' },
  { value: 'events', labelKey: 'Events' },
  { value: 'local-places', labelKey: 'Local Places' },
];

export const FilterTabs = ({ activeTab, onTabChange, counts, t }: FilterTabsProps) => {
  return (
    <div className="flex flex-wrap gap-1.5" role="tablist">
      {TABS.map(({ value, labelKey }) => {
        const isActive = activeTab === value;
        return (
          <Button
            key={value}
            variant="ghost"
            size="sm"
            role="tab"
            aria-selected={isActive}
            onClick={() => onTabChange(value)}
            className={
              isActive
                ? 'bg-surface-accent dark:bg-surface-accent-dark text-content dark:text-content-dark font-semibold'
                : ''
            }
          >
            {t(labelKey)}
            <span className="ml-1.5 rounded-full bg-surface-accent dark:bg-surface-accent-dark px-1.5 py-0.5 text-[10px] font-medium leading-none text-content-secondary dark:text-content-secondary-dark">
              {counts[value]}
            </span>
          </Button>
        );
      })}
    </div>
  );
};
