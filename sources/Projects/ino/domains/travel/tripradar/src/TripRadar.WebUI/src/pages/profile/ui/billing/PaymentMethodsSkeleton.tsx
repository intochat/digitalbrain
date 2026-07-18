export const PaymentMethodsSkeleton = () => (
  <div aria-busy="true">
    <div className="animate-pulse space-y-3">
      {/* Heading placeholder */}
      <div className="h-4 w-32 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />

      {/* Card row 1 */}
      <div className="flex items-center gap-3 py-3">
        <div className="h-8 w-12 rounded bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="flex-1 space-y-1.5">
          <div className="h-3.5 w-28 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-3 w-20 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
        <div className="h-6 w-16 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      </div>

      {/* Card row 2 */}
      <div className="flex items-center gap-3 py-3">
        <div className="h-8 w-12 rounded bg-surface-accent dark:bg-surface-accent-dark" />
        <div className="flex-1 space-y-1.5">
          <div className="h-3.5 w-28 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
          <div className="h-3 w-20 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
        </div>
        <div className="h-6 w-16 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />
      </div>
    </div>
  </div>
);
