import React from 'react';

export interface PreferenceGroupProps {
  title: string;
  icon?: string;
  description?: string;
  children: React.ReactNode;
  className?: string;
}

export const PreferenceGroup = ({ children, className = '' }: PreferenceGroupProps) => {
  return <div className={`space-y-5 ${className}`}>{children}</div>;
};
