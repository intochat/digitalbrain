import { CheckCircle2, Sparkles } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import type { TripVault } from 'entities/tripVault';

interface TripInfoCardProps {
  trip: TripVault;
  activityState: 'active' | 'scheduled' | 'expired' | null;
  isActiveVault: boolean;
  onSetDefault: () => void;
}

const STATUS_BADGE: Record<string, string> = {
  active: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  scheduled: 'bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300',
  expired: 'bg-gray-100 text-gray-600 dark:bg-gray-500/15 dark:text-gray-400',
};

const STATUS_DOT: Record<string, string> = {
  active: 'bg-emerald-500',
  scheduled: 'bg-amber-500',
  expired: 'bg-gray-400 dark:bg-gray-500',
};

const STATUS_LABELS: Record<string, string> = {
  active: 'Active now',
  scheduled: 'Not started yet',
  expired: 'Inactive',
};

export const TripInfoCard = ({ trip, activityState, isActiveVault, onSetDefault }: TripInfoCardProps) => {
  const { t } = useFrontendLanguage();
  const isActive = activityState === 'active';

  return (
    <section className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="space-y-3 flex-1 min-w-0">
          {/* Badges */}
          <div className="flex items-center gap-2 flex-wrap">
            {isActiveVault && (
              <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 dark:bg-emerald-500/15 px-2.5 py-1 text-xs font-medium text-emerald-700 dark:text-emerald-300">
                <CheckCircle2 className="h-3.5 w-3.5" />
                {t('Default vault')}
              </span>
            )}
            {activityState && (
              <>
                <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${STATUS_BADGE[activityState]}`}>
                  {t(STATUS_LABELS[activityState])}
                </span>
                <span
                  className={`inline-flex h-2 w-2 rounded-full ${STATUS_DOT[activityState]}`}
                  aria-label={t(STATUS_LABELS[activityState])}
                />
              </>
            )}
          </div>

          {/* Name */}
          <h4 className="text-sm font-medium text-content dark:text-content-dark break-words">{trip.name}</h4>

          {/* Description */}
          {trip.description && (
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark break-words">
              {trip.description}
            </p>
          )}
        </div>

        {/* Actions */}
        <div className="flex items-center gap-2 self-start shrink-0 pt-1">
          {!isActiveVault && (
            <button
              type="button"
              onClick={onSetDefault}
              disabled={!isActive}
              title={!isActive ? t('Vault is inactive outside selected dates.') : undefined}
              className="inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-medium text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
            >
              <Sparkles className="h-3.5 w-3.5" />
              {t('Set as default')}
            </button>
          )}
        </div>
      </div>
    </section>
  );
};
