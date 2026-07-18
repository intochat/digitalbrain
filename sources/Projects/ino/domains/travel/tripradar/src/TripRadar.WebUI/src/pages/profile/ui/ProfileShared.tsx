import type React from 'react';

export const SectionHeader = ({ children }: { children: React.ReactNode }) => (
  <h3 className="text-xs font-medium uppercase tracking-wider text-content-muted dark:text-content-muted-dark mb-3">
    {children}
  </h3>
);

export interface ProfileRowProps {
  label: string;
  children: React.ReactNode;
  border?: boolean;
}

export const ProfileRow = ({ label, children, border = false }: ProfileRowProps) => (
  <div className={`py-2.5 ${border ? 'border-b border-outline/30 dark:border-outline-dark/30' : ''}`}>
    <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark mb-0.5">
      {label}
    </label>
    {children}
  </div>
);

export const BADGE_VARIANTS = {
  success: 'bg-green-100 dark:bg-green-500/15 text-green-700 dark:text-green-400',
  warning: 'bg-yellow-100 dark:bg-yellow-500/15 text-yellow-700 dark:text-yellow-400',
  info: 'bg-blue-100 dark:bg-blue-500/15 text-blue-700 dark:text-blue-400',
  neutral: 'bg-gray-100 dark:bg-gray-500/15 text-gray-600 dark:text-gray-400',
} as const;

export interface StatusBadgeProps {
  variant: keyof typeof BADGE_VARIANTS;
  label: string;
}

export const StatusBadge = ({ variant, label }: StatusBadgeProps) => (
  <span className={`px-1.5 py-0.5 text-[11px] font-medium rounded ${BADGE_VARIANTS[variant]}`}>{label}</span>
);
