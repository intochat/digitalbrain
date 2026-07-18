export const EventsTableSkeleton = () => (
  <div aria-busy="true">
    <div className="animate-pulse space-y-3">
      {/* Header row */}
      <div className="flex gap-4">
        <div className="h-3 w-20 rounded bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="h-3 w-24 rounded bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="h-3 w-16 rounded bg-surface-accent dark:bg-surface-accent-dark" />
      </div>

      {/* Data rows */}
      {Array.from({ length: 5 }, (_, i) => (
        <div key={i} className="flex gap-4">
          <div className="h-6 w-28 rounded bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-6 w-32 rounded bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-6 w-16 rounded bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
      ))}
    </div>
  </div>
);
