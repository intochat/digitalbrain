import type { ReactNode } from 'react';

interface ChangelogTimelineProps {
  children: ReactNode;
}

export const ChangelogTimeline = ({ children }: ChangelogTimelineProps) => {
  return (
    <section className="px-4 py-10 sm:px-6 sm:py-12 lg:px-8 lg:py-14">
      <div className="mx-auto max-w-2xl">
        <div className="flex flex-col divide-y divide-outline/30 dark:divide-outline-dark/30">{children}</div>
      </div>
    </section>
  );
};
