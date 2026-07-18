export const TripVaultCardSkeleton = () => (
  <div className="animate-pulse rounded-xl border border-outline/30 dark:border-outline-dark/30 bg-surface dark:bg-surface-dark p-4 sm:p-5">
    <div className="flex items-start justify-between gap-3">
      <div className="flex-1 space-y-2">
        <div className="h-4 w-40 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="h-3 w-64 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      </div>
      <div className="h-4 w-16 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
    </div>
    <div className="flex items-center gap-4 mt-3">
      <div className="h-3 w-16 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      <div className="h-3 w-32 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      <div className="h-3 w-24 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
    </div>
    <div className="flex items-center gap-3 mt-4 pt-3 border-t border-outline/20 dark:border-outline-dark/20">
      <div className="h-4 w-14 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      <div className="h-4 w-14 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      <div className="h-4 w-10 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
    </div>
  </div>
);
