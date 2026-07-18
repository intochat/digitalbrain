import { useCallback, useMemo, useRef, useState } from 'react';
import { Globe } from 'lucide-react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { useUpdateProfileMutation } from 'entities/user/api';
import { type FrontendLanguage, FRONTEND_LANGUAGES, resolveFrontendLanguage } from 'shared/i18n';
import { cn } from 'shared/lib/utils';
import { useAuthStore } from 'shared/store/auth';

const LANGUAGE_META: Record<FrontendLanguage, { flag: string; label: string }> = {
  en: { flag: '🇬🇧', label: 'English' },
  ru: { flag: '🇷🇺', label: 'Русский' },
};

const STORAGE_KEY = 'tripradar.language';

export const LanguageToggle = ({ className }: { className?: string }) => {
  const { i18n } = useTranslation();
  const { isAuthenticated } = useAuthStore();
  const updateProfileMutation = useUpdateProfileMutation();
  const [isOpen, setIsOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const current = resolveFrontendLanguage(i18n.resolvedLanguage ?? i18n.language);

  const position = useMemo(() => {
    if (!isOpen || !triggerRef.current) return { top: 0, right: 0 };
    const rect = triggerRef.current.getBoundingClientRect();
    return {
      top: rect.bottom + 4,
      right: window.innerWidth - rect.right,
    };
  }, [isOpen]);

  const handleSelect = useCallback(
    (lang: FrontendLanguage) => {
      if (lang === current) {
        setIsOpen(false);
        return;
      }

      void i18n.changeLanguage(lang);
      localStorage.setItem(STORAGE_KEY, lang);

      if (isAuthenticated) {
        updateProfileMutation.mutate({ languageCode: lang });
      }

      setIsOpen(false);
    },
    [current, i18n, isAuthenticated, updateProfileMutation]
  );

  const handleClickOutside = useCallback((e: MouseEvent) => {
    if (triggerRef.current?.contains(e.target as Node) || panelRef.current?.contains(e.target as Node)) return;
    setIsOpen(false);
  }, []);

  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if (e.key === 'Escape') setIsOpen(false);
  }, []);

  // Attach/detach listeners
  const prevOpen = useRef(false);
  if (isOpen && !prevOpen.current) {
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
  } else if (!isOpen && prevOpen.current) {
    document.removeEventListener('mousedown', handleClickOutside);
    document.removeEventListener('keydown', handleKeyDown);
  }
  prevOpen.current = isOpen;

  return (
    <>
      <button
        ref={triggerRef}
        onClick={() => setIsOpen(o => !o)}
        className={cn(
          'p-2 transition-colors duration-150 flex items-center justify-center',
          'min-w-11 min-h-11 md:min-w-10 md:min-h-10',
          'text-content-secondary dark:text-content-secondary-dark',
          'hover:text-content dark:hover:text-content-dark',
          'touch-manipulation',
          className
        )}
        aria-label={`Language: ${LANGUAGE_META[current].label}`}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
      >
        <Globe className="h-4 w-4" />
      </button>

      {isOpen &&
        createPortal(
          <div
            ref={panelRef}
            role="listbox"
            aria-label="Select language"
            className="fixed z-[9999] rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark shadow-lg overflow-hidden"
            style={{ top: position.top, right: position.right, minWidth: 160 }}
          >
            {FRONTEND_LANGUAGES.map(lang => {
              const meta = LANGUAGE_META[lang];
              const isSelected = lang === current;
              return (
                <button
                  key={lang}
                  type="button"
                  role="option"
                  aria-selected={isSelected}
                  onClick={() => handleSelect(lang)}
                  className={cn(
                    'w-full flex items-center gap-2.5 px-3 py-2 text-sm text-left transition-colors',
                    isSelected
                      ? 'text-content dark:text-content-dark font-medium bg-surface-accent/50 dark:bg-surface-accent-dark/50'
                      : 'text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark'
                  )}
                >
                  <span className="text-base leading-none">{meta.flag}</span>
                  <span>{meta.label}</span>
                </button>
              );
            })}
          </div>,
          document.body
        )}
    </>
  );
};
