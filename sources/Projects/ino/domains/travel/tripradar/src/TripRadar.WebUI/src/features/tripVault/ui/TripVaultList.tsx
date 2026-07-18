import { useMemo } from 'react';
import { ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import type { TripVaultItem } from 'entities/tripVault';
import { Dropdown } from 'shared/ui';
import type { DropdownOption } from 'shared/ui';
import { TripVaultCard } from './TripVaultCard';
import { TRIPS_PAGE_SIZES } from './tripVaultUtils';

interface TripVaultListProps {
  trips: TripVaultItem[];
  visibleTrips: TripVaultItem[];
  tripActivityStateByUniqueId: Map<string, string>;
  activeTripVaultUniqueId: string | null;
  isAnyMutationPending: boolean;
  isFetching: boolean;
  tripsPage: number;
  tripsPageSize: number;
  totalTripsPages: number;
  onSetTripsPage: (page: number) => void;
  onSetTripsPageSize: (size: number) => void;
  onSetDefault: (uniqueId: string) => void;
  onOpenHistory: (uniqueId: string) => void;
  onEdit: (trip: TripVaultItem) => void;
  onDelete: (trip: TripVaultItem) => Promise<void>;
  onRefresh: () => void;
}

export const TripVaultList = ({
  trips,
  visibleTrips,
  tripActivityStateByUniqueId,
  activeTripVaultUniqueId,
  isAnyMutationPending,
  isFetching,
  tripsPage,
  tripsPageSize,
  totalTripsPages,
  onSetTripsPage,
  onSetTripsPageSize,
  onSetDefault,
  onOpenHistory,
  onEdit,
  onDelete,
  onRefresh,
}: TripVaultListProps) => {
  const { t } = useFrontendLanguage();

  const pageSizeOptions: DropdownOption<number>[] = useMemo(
    () => TRIPS_PAGE_SIZES.map(size => ({ value: size, label: `${size} / ${t('page')}` })),
    [t]
  );

  return (
    <div className="space-y-3">
      {visibleTrips.map(trip => {
        const activityState = (tripActivityStateByUniqueId.get(trip.uniqueId) ?? 'active') as
          | 'active'
          | 'scheduled'
          | 'expired';
        const isActiveForSearch = activeTripVaultUniqueId === trip.uniqueId && activityState === 'active';
        return (
          <TripVaultCard
            key={trip.uniqueId}
            trip={trip}
            activityState={activityState}
            isActiveForSearch={isActiveForSearch}
            isAnyMutationPending={isAnyMutationPending}
            onSetDefault={() => onSetDefault(trip.uniqueId)}
            onOpenHistory={() => onOpenHistory(trip.uniqueId)}
            onEdit={() => onEdit(trip)}
            onDelete={() => onDelete(trip)}
          />
        );
      })}

      {/* Footer: pagination + page size + refresh */}
      {(totalTripsPages > 1 || trips.length > TRIPS_PAGE_SIZES[0]) && (
        <div className="flex items-center justify-between pt-2">
          <div className="flex items-center gap-3">
            <div className="w-[120px]">
              <Dropdown
                value={tripsPageSize}
                options={pageSizeOptions}
                onChange={size => {
                  onSetTripsPageSize(size);
                  onSetTripsPage(1);
                }}
                aria-label={t('Trips page size')}
                className="!py-1 !px-2 !text-[11px]"
              />
            </div>
            <button
              type="button"
              onClick={onRefresh}
              className="p-1.5 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
              aria-label={t('Refresh')}
            >
              <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            </button>
          </div>

          {totalTripsPages > 1 && (
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={() => onSetTripsPage(Math.max(1, tripsPage - 1))}
                disabled={tripsPage <= 1}
                className="p-1 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                aria-label={t('Previous page')}
              >
                <ChevronLeft className="h-3.5 w-3.5" />
              </button>
              <div className="flex items-center gap-1 px-1">
                {Array.from({ length: totalTripsPages }, (_, i) => (
                  <button
                    key={i}
                    type="button"
                    onClick={() => onSetTripsPage(i + 1)}
                    className={`h-1.5 rounded-full transition-all ${
                      i + 1 === tripsPage
                        ? 'w-4 bg-content dark:bg-content-dark'
                        : 'w-1.5 bg-content-muted/30 dark:bg-content-muted-dark/30 hover:bg-content-muted/50 dark:hover:bg-content-muted-dark/50'
                    }`}
                    aria-label={`${t('Page')} ${i + 1}`}
                  />
                ))}
              </div>
              <button
                type="button"
                onClick={() => onSetTripsPage(Math.min(totalTripsPages, tripsPage + 1))}
                disabled={tripsPage >= totalTripsPages}
                className="p-1 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                aria-label={t('Next page')}
              >
                <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
