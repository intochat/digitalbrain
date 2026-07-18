import { useState } from 'react';
import { CheckCircle, Link as LinkIcon } from 'lucide-react';

export interface SectionHeadingProps {
  id: string;
  icon?: React.ComponentType<{ className?: string }>;
  children: React.ReactNode;
  className?: string;
}

export const SectionHeading = ({ id, icon: Icon, children, className }: SectionHeadingProps) => {
  const [copied, setCopied] = useState(false);

  const copyAnchor = () => {
    const url = `${window.location.origin}${window.location.pathname}#${id}`;
    navigator.clipboard.writeText(url).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    });
  };

  return (
    <div className="group flex items-center space-x-3 mb-4">
      {Icon && <Icon className="h-5 w-5 text-content-muted dark:text-content-muted-dark" />}
      <h2 className={className ?? 'text-base font-medium text-content dark:text-content-dark'}>{children}</h2>
      <button
        type="button"
        onClick={copyAnchor}
        aria-label="Copy link to section"
        className="opacity-0 group-hover:opacity-100 transition-opacity duration-200 p-1 rounded hover:bg-surface dark:hover:bg-surface-dark-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      >
        {copied ? (
          <CheckCircle className="h-4 w-4 text-green-500" />
        ) : (
          <LinkIcon className="h-4 w-4 text-content-secondary dark:text-content-secondary-dark" />
        )}
      </button>
    </div>
  );
};
