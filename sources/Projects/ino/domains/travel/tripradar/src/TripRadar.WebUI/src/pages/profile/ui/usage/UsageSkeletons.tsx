const CHART_BAR_HEIGHTS = [45, 70, 30, 85, 55, 40, 90, 60, 35, 75, 50, 65, 80, 45, 55, 70, 38];

export const UsageBalanceSkeleton = () => (
  <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
    {[0, 1].map(i => (
      <div
        key={i}
        className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-6 animate-pulse"
      >
        <div className="h-4 w-2/5 bg-outline/50 dark:bg-outline-dark/50 rounded" />
        <div className="mt-3 h-8 w-1/3 bg-outline/50 dark:bg-outline-dark/50 rounded" />
        <div className="mt-5 h-3 rounded-full bg-slate-200 dark:bg-slate-700/70" />
        <div className="mt-4 h-3 w-1/4 bg-outline/50 dark:bg-outline-dark/50 rounded" />
      </div>
    ))}
  </div>
);

export const UsageChartSkeleton = () => (
  <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-6 animate-pulse">
    <div className="mb-5 flex items-center justify-between">
      <div className="h-4 w-28 bg-outline/50 dark:bg-outline-dark/50 rounded" />
      <div className="h-8 w-36 bg-outline/50 dark:bg-outline-dark/50 rounded-full" />
    </div>
    <div className="h-[18rem] border border-outline/60 dark:border-outline-dark/60 rounded-xl p-4 sm:p-5">
      <div className="h-full flex items-end gap-[2px] sm:gap-1">
        {CHART_BAR_HEIGHTS.map((h, i) => (
          <div
            key={i}
            className="flex-1 min-w-[4px] rounded-t-sm bg-outline/50 dark:bg-outline-dark/50"
            style={{ height: `${h}%` }}
          />
        ))}
      </div>
    </div>
    <div className="mt-4 flex items-center justify-between">
      <div className="h-3 w-16 bg-outline/50 dark:bg-outline-dark/50 rounded" />
      <div className="h-3 w-16 bg-outline/50 dark:bg-outline-dark/50 rounded" />
    </div>
    <div className="mt-4 flex justify-center gap-5">
      {[0, 1, 2].map(i => (
        <div key={i} className="flex items-center gap-2">
          <div className="h-2.5 w-2.5 rounded-sm bg-outline/50 dark:bg-outline-dark/50" />
          <div className="h-3 w-14 bg-outline/50 dark:bg-outline-dark/50 rounded" />
        </div>
      ))}
    </div>
  </div>
);
