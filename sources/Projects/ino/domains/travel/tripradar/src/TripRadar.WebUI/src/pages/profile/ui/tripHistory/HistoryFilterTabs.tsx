import { useFrontendLanguage } from 'app/providers';
import { Button } from 'shared/ui';
import type { HistoryFilterTab } from './tripHistoryUtils';
import { getHistoryFilterTabs } from './tripHistoryUtils';

interface HistoryFilterTabsProps {
  activeTab: HistoryFilterTab;
  counts: Record<HistoryFilterTab, number>;
  onTabChange: (tab: HistoryFilterTab) => void;
}

export const HistoryFilterTabs = ({ activeTab, counts, onTabChange }: HistoryFilterTabsProps) => {
  const { t } = useFrontendLanguage();
  const tabs = getHistoryFilterTabs();

  // Only show tabs that have items (except "all" which always shows)
  const visibleTabs = tabs.filter(tab => tab.value === 'all' || counts[tab.value] > 0);

  // Don't render tabs if there's only "all" (no variety in service types)
  if (visibleTabs.length <= 1) return null;

  return (
    <div className="flex flex-wrap gap-1.5 overflow-x-auto" role="tablist">
      {visibleTabs.map(({ value, labelKey }) => {
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
