export const InvoiceHistorySkeleton = () => (
  <div aria-busy="true">
    <div className="animate-pulse space-y-3">
      {/* Heading placeholder */}
      <div className="h-4 w-28 rounded-md bg-surface-accent dark:bg-surface-accent-dark" />

      {/* Table row 1 */}
      <div className="h-8 w-full rounded bg-surface-accent dark:bg-surface-accent-dark" />

      {/* Table row 2 */}
      <div className="h-8 w-full rounded bg-surface-accent dark:bg-surface-accent-dark" />

      {/* Table row 3 */}
      <div className="h-8 w-3/4 rounded bg-surface-accent dark:bg-surface-accent-dark" />
    </div>
  </div>
);
