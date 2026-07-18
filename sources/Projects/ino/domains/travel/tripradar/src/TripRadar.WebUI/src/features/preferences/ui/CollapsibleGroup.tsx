import React, { useRef, useEffect, useCallback, useId } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { GroupHeader } from './GroupHeader';
import './CollapsibleGroup.css';

export interface CollapsibleGroupProps {
  title: string;
  isExpanded: boolean;
  onToggle: () => void;
  children: React.ReactNode;
  className?: string;
}

export const CollapsibleGroup = React.memo(
  ({ title, isExpanded, onToggle, children, className = '' }: CollapsibleGroupProps) => {
    const { t } = useFrontendLanguage();
    const contentRef = useRef<HTMLDivElement>(null);
    const contentId = useId();
    const headerId = useId();

    useEffect(() => {
      const content = contentRef.current;
      if (!content) return;

      const animate = () => {
        if (isExpanded) {
          content.style.height = '0px';
          content.style.opacity = '0';
          void content.offsetHeight;
          content.style.height = `${content.scrollHeight}px`;
          content.style.opacity = '1';

          const cleanup = () => {
            content.style.height = 'auto';
            content.style.willChange = 'auto';
          };
          content.addEventListener('transitionend', cleanup, { once: true });
        } else {
          content.style.height = `${content.scrollHeight}px`;
          content.style.willChange = 'height, opacity';
          void content.offsetHeight;
          content.style.height = '0px';
          content.style.opacity = '0';
        }
      };

      requestAnimationFrame(animate);
    }, [isExpanded]);

    const handleToggle = useCallback(() => {
      onToggle();
    }, [onToggle]);

    const handleKeyDown = useCallback(
      (event: React.KeyboardEvent) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          handleToggle();
        }
      },
      [handleToggle]
    );

    return (
      <div className={className} role="group" aria-labelledby={headerId}>
        <GroupHeader
          title={title}
          isExpanded={isExpanded}
          onClick={handleToggle}
          onKeyDown={handleKeyDown}
          id={headerId}
          aria-controls={contentId}
        />

        <div
          ref={contentRef}
          id={contentId}
          className="collapsible-content overflow-hidden transition-all duration-200 ease-in-out"
          style={{
            height: isExpanded ? 'auto' : '0px',
            opacity: isExpanded ? 1 : 0,
          }}
          aria-hidden={!isExpanded}
          aria-labelledby={headerId}
          role="region"
        >
          <div className="pt-2 pl-4 ml-2">
            <div className="sr-only" aria-live="polite" aria-atomic="true">
              {isExpanded
                ? t('{title} section expanded', { title: t(title) })
                : t('{title} section collapsed', { title: t(title) })}
            </div>
            {children}
          </div>
        </div>
      </div>
    );
  }
);
