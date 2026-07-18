import { useEffect, useState } from 'react';
import { ChevronUp } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

export const BackToTopButton = () => {
  const { t } = useFrontendLanguage();
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    let ticking = false;
    const onScroll = () => {
      if (!ticking) {
        requestAnimationFrame(() => {
          setVisible(window.scrollY > 300);
          ticking = false;
        });
        ticking = true;
      }
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <button
      type="button"
      onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
      aria-label={t('Back to top')}
      className={`fixed bottom-8 right-8 z-50 rounded-full p-3 shadow-lg
        bg-surface dark:bg-surface-dark-secondary
        border border-outline dark:border-outline-dark
        text-content dark:text-content-dark
        hover:bg-surface-accent dark:hover:bg-surface-accent-dark
        focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary
        transition-opacity duration-300
        ${visible ? 'opacity-100 pointer-events-auto' : 'opacity-0 pointer-events-none'}`}
    >
      <ChevronUp className="h-5 w-5" />
    </button>
  );
};
