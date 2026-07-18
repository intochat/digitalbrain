export const PreferencesSkeleton = () => (
  <div aria-busy="true">
    <div className="animate-pulse space-y-6">
      {/* Global Preferences category header */}
      <div className="space-y-3">
        <div className="h-5 w-40 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        {/* Toggle row */}
        <div className="flex items-center gap-3">
          <div className="h-5 w-9 rounded-full bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-3.5 w-28 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
      </div>

      {/* Category section 1 */}
      <div className="space-y-3">
        <div className="h-5 w-32 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="h-3 w-56 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        {/* Collapsible group headers */}
        <div className="space-y-2">
          <div className="h-9 w-full rounded-lg bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-9 w-full rounded-lg bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
      </div>

      {/* Category section 2 */}
      <div className="space-y-3">
        <div className="h-5 w-36 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="h-3 w-48 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        {/* Collapsible group headers */}
        <div className="space-y-2">
          <div className="h-9 w-full rounded-lg bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-9 w-full rounded-lg bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
      </div>

      {/* Save button placeholder */}
      <div className="flex justify-end pt-4 border-t border-outline dark:border-outline-dark">
        <div className="h-8 w-32 rounded-lg bg-surface-accent dark:bg-surface-accent-dark" />
      </div>
    </div>
  </div>
);
