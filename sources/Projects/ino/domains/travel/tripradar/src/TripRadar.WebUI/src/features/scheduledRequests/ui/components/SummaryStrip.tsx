interface SummaryStripProps {
  activeCount: number;
  pausedCount: number;
  nextExecutionTime: string | null;
  t: (key: string, params?: Record<string, string | number>) => string;
}

export const SummaryStrip = ({ activeCount, pausedCount, nextExecutionTime, t }: SummaryStripProps) => {
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-xl bg-surface-accent dark:bg-surface-accent-dark px-4 py-2.5 text-sm text-on-surface-variant dark:text-on-surface-variant-dark">
      <span>
        <span className="font-medium text-on-surface dark:text-on-surface-dark">{activeCount}</span> {t('active')}
      </span>
      <span aria-hidden="true">·</span>
      <span>
        <span className="font-medium text-on-surface dark:text-on-surface-dark">{pausedCount}</span> {t('paused')}
      </span>
      {nextExecutionTime && (
        <>
          <span aria-hidden="true">·</span>
          <span>
            {t('Next run')}:{' '}
            <span className="font-medium text-on-surface dark:text-on-surface-dark">{nextExecutionTime}</span>
          </span>
        </>
      )}
    </div>
  );
};
