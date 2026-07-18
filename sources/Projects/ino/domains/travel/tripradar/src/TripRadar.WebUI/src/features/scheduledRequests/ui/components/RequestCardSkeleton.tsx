export const RequestCardSkeleton = () => {
  return (
    <div className="animate-pulse rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5">
      <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
        <div className="flex-1 space-y-4">
          {/* Badge placeholders */}
          <div className="flex items-center gap-3">
            <div className="h-6 w-16 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
            <div className="h-6 w-20 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
          </div>

          {/* Title bar */}
          <div className="space-y-2">
            <div className="h-5 w-3/5 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
            <div className="h-3 w-24 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
          </div>

          {/* Detail bars */}
          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-2.5">
            <div className="h-5 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
            <div className="h-5 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
          </div>
        </div>

        {/* Action area */}
        <div className="flex items-center gap-2 self-start pt-1">
          <div className="h-9 w-16 rounded-xl bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-9 w-20 rounded-xl bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-9 w-9 rounded-xl bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
      </div>
    </div>
  );
};
