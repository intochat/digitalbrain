import { type FormEvent, useState } from 'react';
import { Star } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useCreateFeedbackMutation, useFeedbackCategoriesQuery, type FeedbackCategoryType } from 'entities/feedback';
import { Button, Dropdown, Textarea } from 'shared/ui';
import type { DropdownOption } from 'shared/ui';
import {
  DEFAULT_CATEGORY_OPTIONS,
  type FeedbackCategoryOption,
  formatCategoryName,
  resolveFeedbackCategoryType,
} from '../model/helpers';

interface FormState {
  content: string;
  feedbackCategoryType: FeedbackCategoryType;
  rating: number | null;
}

const createInitialState = (): FormState => ({
  content: '',
  feedbackCategoryType: 'general',
  rating: null,
});

const FieldError = ({ message }: { message?: string }) => (
  <div className="min-h-[18px]">
    {message && (
      <p className="text-xs text-red-600 dark:text-red-400" role="alert">
        {message}
      </p>
    )}
  </div>
);

export const FeedbackForm = () => {
  const { t } = useFrontendLanguage();
  const { showError, showSuccess } = useToast();
  const [form, setForm] = useState<FormState>(createInitialState);
  const [contentError, setContentError] = useState<string | undefined>();
  const [ratingError, setRatingError] = useState<string | undefined>();

  const categoriesQuery = useFeedbackCategoriesQuery();
  const createMutation = useCreateFeedbackMutation();

  const categoryOptions: FeedbackCategoryOption[] = (() => {
    const fromApi =
      categoriesQuery.data
        ?.map(c => c.name?.trim())
        .filter((n): n is string => Boolean(n))
        .map(name => {
          const type = resolveFeedbackCategoryType(name);
          return type ? { value: type, label: formatCategoryName(name) } : null;
        })
        .filter((c): c is FeedbackCategoryOption => Boolean(c)) ?? [];
    if (fromApi.length === 0) return DEFAULT_CATEGORY_OPTIONS;
    return Array.from(new Map(fromApi.map(c => [c.value, c])).values());
  })();

  const update = (field: keyof FormState, value: string | number | null) => {
    setForm(prev => ({ ...prev, [field]: value }));
    if (field === 'content') setContentError(undefined);
    if (field === 'rating') setRatingError(undefined);
  };

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setContentError(undefined);
    setRatingError(undefined);

    let hasError = false;

    if (form.content.trim().length < 10) {
      setContentError(t('Feedback must contain at least 10 characters.'));
      hasError = true;
    }

    if (form.rating === null) {
      setRatingError(t('Please select a rating.'));
      hasError = true;
    }

    if (hasError) return;

    try {
      await createMutation.mutateAsync({
        title: form.content.trim().slice(0, 120),
        content: form.content.trim(),
        rating: form.rating!,
        feedbackCategoryType: form.feedbackCategoryType,
      });
      setForm({ ...createInitialState(), feedbackCategoryType: form.feedbackCategoryType });
      showSuccess(t('Feedback sent'), t('Thank you. Your feedback has been recorded.'));
    } catch (error) {
      const apiError = error as { response?: { data?: { errors?: Record<string, string[]> } } };
      const validationErrors = apiError?.response?.data?.errors;
      if (validationErrors) {
        const firstMessage = Object.values(validationErrors).flat()[0];
        if (firstMessage) {
          showError(t('Validation error'), firstMessage);
          return;
        }
      }
      showError(t('Failed to send feedback'), t('Try again in a moment.'));
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-content dark:text-content-dark">{t('Category')}</span>
        <Dropdown
          value={form.feedbackCategoryType}
          options={categoryOptions.map(
            o => ({ value: o.value, label: t(o.label) }) as DropdownOption<FeedbackCategoryType>
          )}
          onChange={v => update('feedbackCategoryType', v)}
          disabled={categoriesQuery.isLoading || createMutation.isPending}
          aria-label={t('Feedback category')}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-content dark:text-content-dark">{t('Details')}</span>
        <Textarea
          value={form.content}
          onChange={e => update('content', e.target.value)}
          placeholder={t('Describe your experience, issue, or idea.')}
          rows={4}
          maxLength={2000}
          disabled={createMutation.isPending}
        />
        <div className="flex items-start justify-between gap-3">
          <FieldError message={contentError} />
          <span className="text-xs text-content-muted dark:text-content-muted-dark shrink-0">
            {form.content.length}/2000
          </span>
        </div>
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-content dark:text-content-dark">{t('Rating')}</span>
        <div className="flex items-center gap-1">
          {[1, 2, 3, 4, 5].map(v => (
            <button
              key={v}
              type="button"
              onClick={() => update('rating', form.rating === v ? null : v)}
              disabled={createMutation.isPending}
              aria-pressed={form.rating !== null && v <= form.rating}
              aria-label={t('Rate {starsCount} stars', { starsCount: v })}
              className="rounded-lg p-1.5 text-content-muted dark:text-content-muted-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors duration-150 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <Star
                className={`h-4 w-4 ${form.rating !== null && v <= form.rating ? 'fill-amber-400 text-amber-400 dark:fill-amber-300 dark:text-amber-300' : ''}`}
              />
            </button>
          ))}
          {form.rating !== null && (
            <button
              type="button"
              onClick={() => update('rating', null)}
              className="ml-1 text-xs text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark"
            >
              {t('Clear')}
            </button>
          )}
        </div>
        <FieldError message={ratingError} />
      </div>

      <Button type="submit" isLoading={createMutation.isPending} className="w-full">
        {t('Send feedback')}
      </Button>
    </form>
  );
};
