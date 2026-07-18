export const HistoryListSkeleton = () => (
  <div className="space-y-3">
    {[1, 2, 3].map(i => (
      <div
        key={i}
        className="animate-pulse rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4"
      >
        <div className="flex items-center gap-2 mb-3">
          <div className="h-5 w-20 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-3 w-28 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
        <div className="space-y-2">
          <div className="h-4 w-3/4 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-3 w-1/2 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
      </div>
    ))}
  </div>
);
