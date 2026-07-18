import type { ReactNode } from 'react';

interface SectionSkeletonProps {
  children: ReactNode;
}

export const SectionSkeleton = ({ children }: SectionSkeletonProps) => (
  <div className="border border-outline dark:border-outline-dark rounded-xl p-6">{children}</div>
);
