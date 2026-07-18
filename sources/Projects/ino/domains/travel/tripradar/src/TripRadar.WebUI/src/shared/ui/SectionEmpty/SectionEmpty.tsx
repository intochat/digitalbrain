import type { ReactNode } from 'react';

interface SectionEmptyProps {
  message: string;
  icon?: ReactNode;
  action?: ReactNode;
}

export const SectionEmpty = ({ message, icon, action }: SectionEmptyProps) => (
  <div className="border border-outline dark:border-outline-dark rounded-xl p-6">
    <div className="flex flex-col items-center justify-center py-4 text-center">
      {icon && <div className="mb-2">{icon}</div>}
      <p className="text-sm text-content-secondary dark:text-content-secondary-dark">{message}</p>
      {action && <div className="mt-3">{action}</div>}
    </div>
  </div>
);
