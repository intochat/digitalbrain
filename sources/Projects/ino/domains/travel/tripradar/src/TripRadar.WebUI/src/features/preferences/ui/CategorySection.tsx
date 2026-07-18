import React, { useId } from 'react';
import { useFrontendLanguage } from 'app/providers';

export interface CategorySectionProps {
  title: string;
  description?: string;
  children: React.ReactNode;
  className?: string;
}

export const CategorySection = React.memo(({ title, description, children, className = '' }: CategorySectionProps) => {
  const { t } = useFrontendLanguage();
  const headerId = useId();
  const descriptionId = useId();

  return (
    <section
      className={`space-y-3 ${className}`}
      aria-labelledby={headerId}
      aria-describedby={description ? descriptionId : undefined}
    >
      <div>
        <h3 id={headerId} className="text-sm font-medium text-content dark:text-content-dark">
          {t(title)}
        </h3>
        {description && (
          <p id={descriptionId} className="text-xs text-content-muted dark:text-content-muted-dark mt-0.5">
            {t(description)}
          </p>
        )}
      </div>

      <div className="space-y-1" role="group" aria-labelledby={headerId}>
        {children}
      </div>
    </section>
  );
});
