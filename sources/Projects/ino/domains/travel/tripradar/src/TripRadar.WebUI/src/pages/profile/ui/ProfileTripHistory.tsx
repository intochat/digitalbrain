import { useEffect, useMemo, useState } from 'react';
import { ArrowLeft, Map as MapIcon } from 'lucide-react';
import { useNavigate, useParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useSubscriptionQuery } from 'entities/payment/api';
import { usePrivacyModeQuery } from 'entities/preferences/api';
import {
  getTripVaultActivityState,
  type TripHistoryItem,
  useRemoveTripItemMutation,
  useTripQueryHistoryQuery,
  useTripVaultsQuery,
} from 'entities/tripVault';
import { SectionEmpty } from 'shared/ui';
import { ProfileLayout } from './ProfileLayout';
import {
  ACTIVE_TRIP_VAULT_STORAGE_KEY,
  computeHistoryFilterCounts,
  filterHistoryItems,
  HistoryFilterTabs,
  HistoryList,
  HistoryToolbar,
  HISTORY_PAGE_SIZES,
  TripInfoCard,
} from './tripHistory';
import type { HistoryFilterTab } from './tripHistory';

export const ProfileTripHistory = () => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const { tripUniqueId } = useParams<{ tripUniqueId: string }>();
  const { showError, showSuccess } = useToast();
  const subscriptionQuery = useSubscriptionQuery();
  const privacyModeQuery = usePrivacyModeQuery();
  const isPaidUser =
    !subscriptionQuery.isLoading &&
    !subscriptionQuery.isError &&
    Boolean(subscriptionQuery.data) &&
    subscriptionQuery.data.tierType.toLowerCase() !== 'basic';
  const noTraceEnabled = isPaidUser && !privacyModeQuery.isError && (privacyModeQuery.data?.enabled ?? false);

  const selectedTripUniqueId = tripUniqueId ?? null;
  const [historyPage, setHistoryPage] = useState(1);
  const [historyPageSize, setHistoryPageSize] = useState(HISTORY_PAGE_SIZES[1]);
  const [historyFilter, setHistoryFilter] = useState<HistoryFilterTab>('all');
  const [activeTripVaultUniqueId, setActiveTripVaultUniqueId] = useState<string | null>(() => {
    if (typeof window === 'undefined') return null;
    return window.localStorage.getItem(ACTIVE_TRIP_VAULT_STORAGE_KEY);
  });

  const tripVaultsQuery = useTripVaultsQuery({ enabled: isPaidUser });
  const removeTripItemMutation = useRemoveTripItemMutation();
  const tripHistoryQuery = useTripQueryHistoryQuery({
    tripUniqueId: selectedTripUniqueId,
    pageNumber: historyPage,
    pageSize: historyPageSize,
    enabled: isPaidUser,
  });

  const selectedTrip = useMemo(
    () => tripVaultsQuery.data?.find(trip => trip.uniqueId === selectedTripUniqueId) ?? null,
    [selectedTripUniqueId, tripVaultsQuery.data]
  );

  const activityState = useMemo(() => (selectedTrip ? getTripVaultActivityState(selectedTrip) : null), [selectedTrip]);

  const isActiveVault = Boolean(
    selectedTripUniqueId && activeTripVaultUniqueId === selectedTripUniqueId && activityState === 'active'
  );

  const historyFilterCounts = useMemo(
    () => computeHistoryFilterCounts(tripHistoryQuery.data?.items ?? []),
    [tripHistoryQuery.data?.items]
  );
  const filteredHistoryData = useMemo(() => {
    if (!tripHistoryQuery.data) return undefined;
    const filtered = filterHistoryItems(tripHistoryQuery.data.items, historyFilter);
    return { ...tripHistoryQuery.data, items: filtered };
  }, [tripHistoryQuery.data, historyFilter]);

  useEffect(() => {
    setHistoryPage(1);
  }, [selectedTripUniqueId, historyPageSize]);

  useEffect(() => {
    if (!tripHistoryQuery.data) return;
    if (historyPage > tripHistoryQuery.data.totalPages) {
      setHistoryPage(Math.max(1, tripHistoryQuery.data.totalPages));
    }
  }, [historyPage, tripHistoryQuery.data]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    if (activeTripVaultUniqueId) {
      window.localStorage.setItem(ACTIVE_TRIP_VAULT_STORAGE_KEY, activeTripVaultUniqueId);
    } else {
      window.localStorage.removeItem(ACTIVE_TRIP_VAULT_STORAGE_KEY);
    }
  }, [activeTripVaultUniqueId]);

  const handleRemoveHistoryItem = async (item: TripHistoryItem) => {
    if (!selectedTripUniqueId) return;
    try {
      await removeTripItemMutation.mutateAsync({
        tripUniqueId: selectedTripUniqueId,
        itemUniqueId: item.uniqueId,
      });
      showSuccess(t('Item deleted'), t('History item has been removed.'));
    } catch (error) {
      console.error('Failed to remove trip history item:', error);
      showError(t('Delete failed'), t('Unable to remove this history item. Please try again.'));
    }
  };

  const backButton = (
    <button
      type="button"
      onClick={() => navigate('/profile/trips')}
      className="inline-flex items-center gap-1.5 text-sm text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark transition-colors"
    >
      <ArrowLeft className="h-4 w-4" />
      {t('Back to trips')}
    </button>
  );

  return (
    <ProfileLayout>
      <div className="px-4 sm:px-6 lg:px-8 pb-4 sm:pb-6 lg:pb-8 space-y-4">
        {backButton}

        {/* Not paid */}
        {!isPaidUser && (
          <SectionEmpty
            message={t('Trip history is available only for paid users.')}
            icon={<MapIcon className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
          />
        )}

        {isPaidUser && (
          <>
            {noTraceEnabled && (
              <div className="flex items-center gap-3 rounded-lg border border-amber-200/60 dark:border-amber-500/15 bg-amber-50/40 dark:bg-amber-500/5 px-4 py-2.5">
                <div className="h-1.5 w-1.5 rounded-full bg-amber-500 dark:bg-amber-400 flex-shrink-0" />
                <p className="text-[13px] text-amber-700 dark:text-amber-300/90 leading-relaxed">
                  {t('No-trace mode is enabled: new requests are not saved to trip history or vaults.')}
                </p>
              </div>
            )}

            {selectedTrip && (
              <TripInfoCard
                trip={selectedTrip}
                activityState={activityState}
                isActiveVault={isActiveVault}
                onSetDefault={() => setActiveTripVaultUniqueId(selectedTrip.uniqueId)}
              />
            )}

            {/* Query History header + toolbar */}
            <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <h3 className="text-[13px] font-medium text-content dark:text-content-dark">{t('Query History')}</h3>
                <p className="text-xs text-content-secondary dark:text-content-secondary-dark mt-0.5">
                  {t('Remove stale entries and keep vault context relevant for future AI searches.')}
                </p>
              </div>
              <HistoryToolbar
                pageSize={historyPageSize}
                isFetching={tripHistoryQuery.isFetching}
                onPageSizeChange={setHistoryPageSize}
                onRefresh={() => tripHistoryQuery.refetch()}
              />
            </div>

            <HistoryFilterTabs activeTab={historyFilter} counts={historyFilterCounts} onTabChange={setHistoryFilter} />

            <HistoryList
              tripUniqueId={selectedTripUniqueId}
              isLoading={tripHistoryQuery.isLoading}
              isError={tripHistoryQuery.isError}
              data={filteredHistoryData}
              onDelete={handleRemoveHistoryItem}
              onPageChange={setHistoryPage}
              onRefresh={() => tripHistoryQuery.refetch()}
            />
          </>
        )}
      </div>
    </ProfileLayout>
  );
};
