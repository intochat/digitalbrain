import React from 'react';
import { useFrontendLanguage } from 'app/providers';

export interface FieldSectionProps {
  title?: string;
  description?: string;
  children: React.ReactNode;
  className?: string;
  variant?: 'default' | 'compact';
}

export const FieldSection = ({
  title,
  description,
  children,
  className = '',
  variant = 'default',
}: FieldSectionProps) => {
  const { t } = useFrontendLanguage();
  const isCompact = variant === 'compact';

  return (
    <div className={`${isCompact ? 'space-y-3' : 'space-y-4'} ${className}`}>
      {title && (
        <div className={`${isCompact ? 'pb-2' : 'pb-3'} border-b border-outline dark:border-outline-dark`}>
          <h4 className="text-sm font-medium text-content dark:text-content-dark">{t(title)}</h4>
          {description && (
            <p className="text-xs text-content-secondary dark:text-content-secondary-dark mt-1 leading-relaxed">
              {t(description)}
            </p>
          )}
        </div>
      )}

      {/* Field grid with improved spacing */}
      <div className={`grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 ${isCompact ? 'lg:gap-3' : 'lg:gap-5'}`}>
        {children}
      </div>
    </div>
  );
};
