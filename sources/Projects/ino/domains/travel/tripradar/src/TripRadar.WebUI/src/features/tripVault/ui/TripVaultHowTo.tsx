import { Copy, Info } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

interface TripVaultHowToProps {
  activeTripName: string | null;
  onCopyName: () => void;
  onCopySnippet: () => void;
}

export const TripVaultHowTo = ({ activeTripName, onCopyName, onCopySnippet }: TripVaultHowToProps) => {
  const { t } = useFrontendLanguage();

  return (
    <div className="rounded-lg border border-outline/30 dark:border-outline-dark/30 bg-surface-accent/20 dark:bg-surface-accent-dark/10 px-4 py-3">
      <div className="flex items-start gap-2.5">
        <Info className="h-3.5 w-3.5 text-content-muted dark:text-content-muted-dark mt-0.5 flex-shrink-0" />
        <div className="min-w-0 flex-1">
          <p className="text-xs text-content-secondary dark:text-content-secondary-dark leading-relaxed">
            {t('One vault is used as default chat context at a time. Mention vault name explicitly in prompts.')}
          </p>
          <div className="flex flex-wrap items-center gap-3 mt-2">
            <span className="text-[11px] text-content-muted dark:text-content-muted-dark">
              {t('Default vault:')}{' '}
              <span className="text-content-secondary dark:text-content-secondary-dark font-medium">
                {activeTripName ?? t('not selected')}
              </span>
            </span>
            {activeTripName && (
              <>
                <button
                  type="button"
                  onClick={onCopyName}
                  className="inline-flex items-center gap-1 text-[11px] text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors"
                >
                  <Copy className="h-3 w-3" />
                  {t('Copy name')}
                </button>
                <button
                  type="button"
                  onClick={onCopySnippet}
                  className="inline-flex items-center gap-1 text-[11px] text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors"
                >
                  <Copy className="h-3 w-3" />
                  {t('Copy prompt snippet')}
                </button>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
