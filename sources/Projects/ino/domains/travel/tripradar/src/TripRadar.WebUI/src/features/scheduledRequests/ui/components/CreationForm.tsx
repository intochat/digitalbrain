import { FormEvent, useState } from 'react';
import { useToast } from 'app/providers/ToastProvider';
import { useCreateScheduledRequestMutation } from 'entities/scheduledRequests';
import { trackEvent } from 'shared/lib';
import { Button } from 'shared/ui';
import type { ScheduledRequestFormState } from '../constants';
import {
  buildCreatePayload,
  createInitialFormState,
  FIRST_TRIP_REQUEST_EVENT_STORAGE_KEY,
  getValidationError,
} from '../utils';
import { RequestForm } from './RequestForm';

interface CreationFormProps {
  isOpen: boolean;
  onClose: () => void;
  onCreated: () => void;
  t: (key: string, params?: Record<string, string | number>) => string;
}

export const CreationForm = ({ isOpen, onClose, onCreated, t }: CreationFormProps) => {
  const [formState, setFormState] = useState<ScheduledRequestFormState>(() => createInitialFormState());
  const createMutation = useCreateScheduledRequestMutation();
  const { showError, showSuccess } = useToast();

  if (!isOpen) return null;

  const handleChange = (field: keyof ScheduledRequestFormState, value: string) => {
    setFormState(prev => ({ ...prev, [field]: value }));
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const error = getValidationError(formState, true);
    if (error) {
      showError(t('Invalid request'), error);
      return;
    }

    try {
      await createMutation.mutateAsync(buildCreatePayload(formState));

      if (typeof window !== 'undefined') {
        const tracked = window.localStorage.getItem(FIRST_TRIP_REQUEST_EVENT_STORAGE_KEY) === '1';
        if (!tracked) {
          trackEvent(
            'first_trip_request',
            { queryType: formState.queryType, schedule: formState.schedule, source: 'scheduled_requests' },
            { stage: 'activation', userState: 'activated' }
          );
          window.localStorage.setItem(FIRST_TRIP_REQUEST_EVENT_STORAGE_KEY, '1');
        }
      }

      showSuccess(t('Scheduled request created'), t('Your new scheduled request is active.'));
      setFormState(createInitialFormState());
      onCreated();
    } catch {
      showError(t('Creation failed'), t('Unable to create scheduled request. Please check your input and try again.'));
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <RequestForm formState={formState} onChange={handleChange} t={t} />
      <div className="flex flex-wrap gap-2 pt-1">
        <Button type="submit" disabled={createMutation.isPending} isLoading={createMutation.isPending}>
          {t('Create Scheduled Request')}
        </Button>
        <Button type="button" variant="secondary" onClick={onClose}>
          {t('Cancel')}
        </Button>
      </div>
    </form>
  );
};
