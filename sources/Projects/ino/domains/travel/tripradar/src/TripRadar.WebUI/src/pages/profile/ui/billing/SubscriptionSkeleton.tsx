export const SubscriptionSkeleton = () => (
  <div aria-busy="true">
    <div className="animate-pulse space-y-4">
      {/* Status dot + plan name */}
      <div className="flex items-center gap-2">
        <div className="h-2 w-2 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="h-4 w-28 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      </div>

      {/* Price */}
      <div className="h-7 w-36 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />

      {/* Billing date */}
      <div className="h-3 w-44 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />

      {/* Toggle row */}
      <div className="flex items-center gap-5">
        <div className="flex items-center gap-2">
          <div className="h-5 w-9 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-3.5 w-24 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
        <div className="flex items-center gap-2">
          <div className="h-5 w-9 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-3.5 w-24 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
      </div>

      {/* Action buttons */}
      <div className="flex gap-2">
        <div className="h-8 w-24 rounded-lg bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="h-8 w-32 rounded-lg bg-surface-accent dark:bg-surface-accent-dark" />
      </div>
    </div>
  </div>
);
