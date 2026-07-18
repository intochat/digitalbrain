import { useEffect, useRef, useState } from 'react';
import { useFrontendLanguage } from 'app/providers';

export interface TocSection {
  id: string;
  title: string;
}

interface TableOfContentsProps {
  sections: TocSection[];
}

export const TableOfContents = ({ sections }: TableOfContentsProps) => {
  const { t } = useFrontendLanguage();
  const [activeId, setActiveId] = useState<string>('');
  const observerRef = useRef<IntersectionObserver | null>(null);

  useEffect(() => {
    if (typeof IntersectionObserver === 'undefined') return;

    observerRef.current = new IntersectionObserver(
      entries => {
        const visible = entries.filter(e => e.isIntersecting);
        if (visible.length > 0) {
          setActiveId(visible[0].target.id);
        }
      },
      { rootMargin: '-80px 0px -60% 0px', threshold: 0 }
    );

    const elements = sections.map(s => document.getElementById(s.id)).filter(Boolean) as HTMLElement[];
    elements.forEach(el => observerRef.current?.observe(el));

    return () => observerRef.current?.disconnect();
  }, [sections]);

  const handleClick = (e: React.MouseEvent<HTMLAnchorElement>, id: string) => {
    e.preventDefault();
    const el = document.getElementById(id);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth' });
      window.history.replaceState(null, '', `#${id}`);
    }
  };

  return (
    <nav
      aria-label={t('Table of contents')}
      className="hidden lg:block sticky top-20 w-56 shrink-0 max-h-[calc(100vh-6rem)] overflow-y-auto"
    >
      <ul className="space-y-1 text-sm">
        {sections.map(section => {
          const isActive = activeId === section.id;
          return (
            <li key={section.id}>
              <a
                href={`#${section.id}`}
                onClick={e => handleClick(e, section.id)}
                className={`block py-1.5 pl-3 border-l-2 transition-colors duration-200
                  focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary rounded-r-sm
                  ${
                    isActive
                      ? 'border-content dark:border-content-dark text-content dark:text-content-dark font-medium'
                      : 'border-transparent text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark hover:border-outline dark:hover:border-outline-dark'
                  }`}
              >
                {t(section.title)}
              </a>
            </li>
          );
        })}
      </ul>
    </nav>
  );
};
