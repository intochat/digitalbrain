import { FormEvent, useState } from 'react';
import type { ScheduledExecutionItem } from 'entities/scheduledRequests';
import { Button } from 'shared/ui';
import type { ScheduledRequestFormState } from '../constants';
import {
  createInitialFormState,
  normalizeAirportCode,
  resolveQueryTypeFromServiceType,
  toDateTimeLocalInputValue,
} from '../utils';
import { RequestForm } from './RequestForm';

interface InlineEditorProps {
  execution: ScheduledExecutionItem;
  onSave: (formState: ScheduledRequestFormState) => Promise<void>;
  onCancel: () => void;
  t: (key: string, params?: Record<string, string | number>) => string;
}

const initFormState = (execution: ScheduledExecutionItem): ScheduledRequestFormState => {
  const queryType = resolveQueryTypeFromServiceType(execution.serviceType);
  const state = createInitialFormState();
  state.queryType = queryType;
  state.schedule = execution.schedule;
  state.nextExecutionTime = toDateTimeLocalInputValue(execution.nextExecutionTime);

  if (queryType === 'flights') {
    state.departureAirportCode = normalizeAirportCode(execution.departureAirportCode ?? '');
    state.destinationAirportCode = normalizeAirportCode(execution.destinationAirportCode ?? '');
    state.departureDate = execution.departureDate?.match(/^\d{4}-\d{2}-\d{2}/)?.[0] ?? '';
    state.returnDate = execution.returnDate?.match(/^\d{4}-\d{2}-\d{2}/)?.[0] ?? '';
  }
  if (queryType === 'hotels') {
    state.location = execution.location ?? '';
    state.checkInDate = execution.checkInDate?.match(/^\d{4}-\d{2}-\d{2}/)?.[0] ?? '';
    state.checkOutDate = execution.checkOutDate?.match(/^\d{4}-\d{2}-\d{2}/)?.[0] ?? '';
  }
  if (queryType === 'events' || queryType === 'local-places') {
    state.searchQuery = execution.searchQuery ?? '';
    state.location = execution.location ?? '';
  }
  if (queryType === 'local-places') {
    state.radius = execution.radius ? String(execution.radius) : '';
  }

  return state;
};

export const InlineEditor = ({ execution, onSave, onCancel, t }: InlineEditorProps) => {
  const [formState, setFormState] = useState<ScheduledRequestFormState>(() => initFormState(execution));
  const [isSaving, setIsSaving] = useState(false);

  const handleChange = (field: keyof ScheduledRequestFormState, value: string) => {
    setFormState(prev => ({ ...prev, [field]: value }));
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSaving(true);
    try {
      await onSave(formState);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <RequestForm formState={formState} onChange={handleChange} disableQueryType t={t} />
      <div className="flex flex-wrap gap-2 pt-1">
        <Button type="submit" disabled={isSaving} isLoading={isSaving}>
          {t('Save Changes')}
        </Button>
        <Button type="button" variant="secondary" onClick={onCancel} disabled={isSaving}>
          {t('Cancel')}
        </Button>
      </div>
    </form>
  );
};
