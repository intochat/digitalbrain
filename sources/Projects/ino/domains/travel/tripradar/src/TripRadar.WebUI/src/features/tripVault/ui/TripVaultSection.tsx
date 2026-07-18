import type { FormEvent } from 'react';
import { useEffect, useMemo, useState } from 'react';
import { Map as MapIcon, Plus } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import {
  getTripVaultActivityState,
  useCreateTripVaultMutation,
  useDeleteTripVaultMutation,
  useTripVaultsQuery,
  useUpdateTripVaultMutation,
  type TripVaultItem,
} from 'entities/tripVault';
import { getProfileTripHistoryRoute } from 'shared/config/routes';
import { trackEvent } from 'shared/lib';
import { Button, SectionEmpty, SectionError } from 'shared/ui';
import { TripVaultCardSkeleton } from './TripVaultCardSkeleton';
import { TripVaultForm } from './TripVaultForm';
import { TripVaultList } from './TripVaultList';
import {
  ACTIVE_TRIP_VAULT_STORAGE_KEY,
  DEFAULT_TRIPS_PAGE_SIZE,
  DUPLICATE_TRIP_NAME_MESSAGE,
  FIRST_SAVED_TRIP_EVENT_STORAGE_KEY,
  TRIP_VAULT_NAME_EXISTS_ERROR_CODE,
  createInitialFormState,
  getApiErrorCode,
  getFormValidationError,
  getTodayDateInputValue,
  normalizeTripName,
  toApiDate,
  toDateInputValue,
} from './tripVaultUtils';
import type { TripVaultFormState } from './tripVaultUtils';

interface TripVaultSectionProps {
  isPaidUser: boolean;
  noTraceEnabled: boolean;
  className?: string;
}

export const TripVaultSection = ({ isPaidUser, noTraceEnabled, className = '' }: TripVaultSectionProps) => {
  const { t } = useFrontendLanguage();
  const { showError, showSuccess } = useToast();
  const navigate = useNavigate();
  const todayDateInputValue = useMemo(() => getTodayDateInputValue(), []);

  const [formState, setFormState] = useState<TripVaultFormState>(() => createInitialFormState());
  const [editingTripUniqueId, setEditingTripUniqueId] = useState<string | null>(null);
  const [isFormVisible, setIsFormVisible] = useState(false);
  const [activeTripVaultUniqueId, setActiveTripVaultUniqueId] = useState<string | null>(() => {
    if (typeof window === 'undefined') return null;
    return window.localStorage.getItem(ACTIVE_TRIP_VAULT_STORAGE_KEY);
  });
  const [tripsPage, setTripsPage] = useState(1);
  const [tripsPageSize, setTripsPageSize] = useState<number>(DEFAULT_TRIPS_PAGE_SIZE);

  const tripVaultsQuery = useTripVaultsQuery({ enabled: isPaidUser });
  const createTripVaultMutation = useCreateTripVaultMutation();
  const updateTripVaultMutation = useUpdateTripVaultMutation();
  const deleteTripVaultMutation = useDeleteTripVaultMutation();

  const trips = useMemo(
    () =>
      [...(tripVaultsQuery.data ?? [])].sort(
        (left, right) => new Date(right.createdOn).getTime() - new Date(left.createdOn).getTime()
      ),
    [tripVaultsQuery.data]
  );

  const tripActivityStateByUniqueId = useMemo(
    () => new Map(trips.map(trip => [trip.uniqueId, getTripVaultActivityState(trip)])),
    [trips]
  );

  const activeTrips = useMemo(
    () => trips.filter(trip => tripActivityStateByUniqueId.get(trip.uniqueId) === 'active'),
    [tripActivityStateByUniqueId, trips]
  );

  const totalTripsPages = useMemo(
    () => Math.max(1, Math.ceil(trips.length / tripsPageSize)),
    [trips.length, tripsPageSize]
  );

  const visibleTrips = useMemo(() => {
    const startIndex = (tripsPage - 1) * tripsPageSize;
    return trips.slice(startIndex, startIndex + tripsPageSize);
  }, [trips, tripsPage, tripsPageSize]);

  const hasData = trips.length > 0;

  useEffect(() => {
    if (tripsPage > totalTripsPages) {
      setTripsPage(totalTripsPages);
      return;
    }
    if (tripsPage < 1) setTripsPage(1);
  }, [totalTripsPages, tripsPage]);

  useEffect(() => {
    if (!activeTripVaultUniqueId && activeTrips.length > 0) {
      setActiveTripVaultUniqueId(activeTrips[0].uniqueId);
      return;
    }
    if (activeTripVaultUniqueId && activeTrips.every(trip => trip.uniqueId !== activeTripVaultUniqueId)) {
      setActiveTripVaultUniqueId(activeTrips[0]?.uniqueId ?? null);
    }
  }, [activeTripVaultUniqueId, activeTrips]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    if (activeTripVaultUniqueId) {
      window.localStorage.setItem(ACTIVE_TRIP_VAULT_STORAGE_KEY, activeTripVaultUniqueId);
    } else {
      window.localStorage.removeItem(ACTIVE_TRIP_VAULT_STORAGE_KEY);
    }
  }, [activeTripVaultUniqueId]);

  const isAnyMutationPending =
    createTripVaultMutation.isPending || updateTripVaultMutation.isPending || deleteTripVaultMutation.isPending;

  const resetForm = () => {
    setFormState(createInitialFormState());
    setEditingTripUniqueId(null);
    setIsFormVisible(false);
  };

  const startEditingTrip = (trip: TripVaultItem) => {
    setFormState({
      name: trip.name,
      description: trip.description ?? '',
      startDate: toDateInputValue(trip.startDate),
      endDate: toDateInputValue(trip.endDate),
    });
    setEditingTripUniqueId(trip.uniqueId);
    setIsFormVisible(true);
  };

  const handleInputChange = (field: keyof TripVaultFormState, value: string) => {
    if (field === 'startDate') {
      setFormState(previous => ({
        ...previous,
        startDate: value,
        endDate: previous.endDate && value && previous.endDate < value ? value : previous.endDate,
      }));
      return;
    }
    setFormState(previous => ({ ...previous, [field]: value }));
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const validationError = getFormValidationError(formState, todayDateInputValue);
    if (validationError) {
      showError(t('Validation error'), validationError);
      return;
    }

    const normalizedFormName = normalizeTripName(formState.name);
    const hasDuplicateName = trips.some(trip => {
      const sameTrip = editingTripUniqueId && trip.uniqueId === editingTripUniqueId;
      return !sameTrip && normalizeTripName(trip.name) === normalizedFormName;
    });

    if (hasDuplicateName) {
      showError(t('Validation error'), DUPLICATE_TRIP_NAME_MESSAGE);
      return;
    }

    const payload = {
      name: formState.name.trim(),
      description: formState.description.trim() || null,
      startDate: toApiDate(formState.startDate),
      endDate: toApiDate(formState.endDate),
    };

    try {
      if (editingTripUniqueId) {
        await updateTripVaultMutation.mutateAsync({ tripUniqueId: editingTripUniqueId, request: payload });
        showSuccess(t('Trip updated'), t('Trip details were updated successfully.'));
      } else {
        await createTripVaultMutation.mutateAsync(payload);
        if (typeof window !== 'undefined') {
          const hasTrackedFirstSavedTrip = window.localStorage.getItem(FIRST_SAVED_TRIP_EVENT_STORAGE_KEY) === '1';
          if (!hasTrackedFirstSavedTrip) {
            trackEvent('first_saved_trip', { source: 'trip_vault' }, { stage: 'activation', userState: 'activated' });
            window.localStorage.setItem(FIRST_SAVED_TRIP_EVENT_STORAGE_KEY, '1');
          }
        }
        showSuccess(t('Trip created'), t('New trip vault has been created.'));
      }
      resetForm();
    } catch (error) {
      console.error('Failed to submit trip vault form:', error);
      const errorCode = getApiErrorCode(error);
      if (errorCode === TRIP_VAULT_NAME_EXISTS_ERROR_CODE) {
        showError(t('Validation error'), DUPLICATE_TRIP_NAME_MESSAGE);
        return;
      }
      showError(t('Request failed'), t('Unable to save trip vault changes. Please try again.'));
    }
  };

  const handleDeleteTrip = async (trip: TripVaultItem) => {
    try {
      await deleteTripVaultMutation.mutateAsync({ tripUniqueId: trip.uniqueId });
      if (editingTripUniqueId === trip.uniqueId) resetForm();
      showSuccess(t('Trip deleted'), t('Trip vault has been removed.'));
    } catch (error) {
      console.error('Failed to delete trip vault:', error);
      showError(t('Delete failed'), t('Unable to delete trip vault. Please try again.'));
    }
  };

  /* ── Not a paid user ── */
  if (!isPaidUser) {
    return (
      <div className={className}>
        <SectionEmpty
          message={t('Trip vaults are available on paid plans')}
          icon={<MapIcon className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
        />
      </div>
    );
  }

  /* ── Loading ── */
  if (tripVaultsQuery.isLoading) {
    return (
      <div className={`space-y-3 ${className}`}>
        <TripVaultCardSkeleton />
        <TripVaultCardSkeleton />
        <TripVaultCardSkeleton />
      </div>
    );
  }

  /* ── Error ── */
  if (tripVaultsQuery.isError) {
    return (
      <div className={className}>
        <SectionError message={t('Unable to load trips')} onRetry={() => tripVaultsQuery.refetch()} />
      </div>
    );
  }

  /* ── Main content ── */
  return (
    <div className={`space-y-4 ${className}`}>
      {noTraceEnabled && (
        <div className="flex items-center gap-3 rounded-lg border border-amber-200/60 dark:border-amber-500/15 bg-amber-50/40 dark:bg-amber-500/5 px-4 py-2.5">
          <div className="h-1.5 w-1.5 rounded-full bg-amber-500 dark:bg-amber-400 flex-shrink-0" />
          <p className="text-[13px] text-amber-700 dark:text-amber-300/90 leading-relaxed">
            {t('No-trace mode is enabled: new requests are not saved to trip history or vaults.')}
          </p>
        </div>
      )}

      {/* Empty state */}
      {!hasData && !isFormVisible && (
        <SectionEmpty
          message={t('No trips yet')}
          icon={<MapIcon className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
          action={
            <Button variant="primary" size="sm" onClick={() => setIsFormVisible(true)}>
              {t('Create your first trip')}
            </Button>
          }
        />
      )}

      {/* Trip cards */}
      {hasData && (
        <TripVaultList
          trips={trips}
          visibleTrips={visibleTrips}
          tripActivityStateByUniqueId={tripActivityStateByUniqueId}
          activeTripVaultUniqueId={activeTripVaultUniqueId}
          isAnyMutationPending={isAnyMutationPending}
          isFetching={tripVaultsQuery.isFetching}
          tripsPage={tripsPage}
          tripsPageSize={tripsPageSize}
          totalTripsPages={totalTripsPages}
          onSetTripsPage={setTripsPage}
          onSetTripsPageSize={setTripsPageSize}
          onSetDefault={setActiveTripVaultUniqueId}
          onOpenHistory={uniqueId => navigate(getProfileTripHistoryRoute(uniqueId))}
          onEdit={startEditingTrip}
          onDelete={handleDeleteTrip}
          onRefresh={() => tripVaultsQuery.refetch()}
        />
      )}

      {/* New Trip button — below the list, like scheduled-requests */}
      {hasData && !isFormVisible && (
        <Button variant="secondary" size="sm" onClick={() => setIsFormVisible(true)} className="gap-1.5">
          <Plus className="h-4 w-4" />
          {t('New Trip')}
        </Button>
      )}

      {/* Collapsible form */}
      {isFormVisible && (
        <TripVaultForm
          formState={formState}
          editingTripUniqueId={editingTripUniqueId}
          isAnyMutationPending={isAnyMutationPending}
          todayDateInputValue={todayDateInputValue}
          onInputChange={handleInputChange}
          onSubmit={handleSubmit}
          onCancel={resetForm}
        />
      )}
    </div>
  );
};
