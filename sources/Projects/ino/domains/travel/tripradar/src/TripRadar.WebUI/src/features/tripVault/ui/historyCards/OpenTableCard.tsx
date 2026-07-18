import { UtensilsCrossed, Star } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import {
  formatRating,
  getArray,
  getNumber,
  getObject,
  getString,
  isTruncatedWrapperPayload,
  safeParse,
  truncateText,
} from './parseHistoryData';

interface OpenTableCardProps {
  item: TripHistoryItem;
}

interface ParsedReview {
  content: string;
  userName: string | null;
  overallRating: number | null;
  dinedAt: string | null;
}

const parseReviews = (data: Record<string, unknown>): ParsedReview[] => {
  const reviews = getArray(data, 'reviews') ?? [];

  return reviews.slice(0, 3).map(review => {
    const r = review as Record<string, unknown>;
    const user = getObject(r, 'user');
    const rating = getObject(r, 'rating') ?? getObject(r, 'ratings');

    return {
      content: getString(r, 'content') ?? '',
      userName: user ? getString(user, 'name') : null,
      overallRating: rating ? getNumber(rating, 'overall') : null,
      dinedAt: getString(r, 'dinedAt') ?? getString(r, 'dined_at'),
    };
  });
};

export const OpenTableCard = ({ item }: OpenTableCardProps) => {
  const data = safeParse(item.resultSummary);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  const reviewsSummary = getObject(data, 'reviewsSummary') ?? getObject(data, 'reviews_summary');
  const ratingsSummary = reviewsSummary
    ? (getObject(reviewsSummary, 'ratingsSummary') ?? getObject(reviewsSummary, 'ratings_summary'))
    : null;

  const overall = ratingsSummary ? getNumber(ratingsSummary, 'overall') : null;
  const food = ratingsSummary ? getNumber(ratingsSummary, 'food') : null;
  const service = ratingsSummary ? getNumber(ratingsSummary, 'service') : null;
  const ambience = ratingsSummary ? getNumber(ratingsSummary, 'ambience') : null;
  const reviewsCount = reviewsSummary
    ? (getNumber(reviewsSummary, 'reviewsCount') ?? getNumber(reviewsSummary, 'reviews_count'))
    : null;

  const awards = getArray(data, 'awards') ?? [];
  const reviews = parseReviews(data);

  const ratingCategories = [
    { label: 'Food', value: food },
    { label: 'Service', value: service },
    { label: 'Ambience', value: ambience },
  ].filter(cat => cat.value != null);

  return (
    <div className="space-y-3">
      {/* Overall rating header */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="inline-flex items-center gap-2 rounded-lg bg-rose-50 dark:bg-rose-500/10 px-3 py-1.5 text-sm font-semibold text-rose-700 dark:text-rose-300">
          <UtensilsCrossed className="h-4 w-4" />
          {overall != null ? formatRating(overall) : 'Restaurant Reviews'}
        </div>
        {reviewsCount != null && (
          <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
            {reviewsCount.toLocaleString()} reviews
          </span>
        )}
      </div>

      {/* Rating breakdown */}
      {ratingCategories.length > 0 && (
        <div className="flex flex-wrap gap-3">
          {ratingCategories.map(cat => (
            <div key={cat.label} className="flex items-center gap-1.5">
              <span className="text-[11px] text-content-secondary dark:text-content-secondary-dark">{cat.label}</span>
              <div className="flex items-center gap-0.5">
                {Array.from({ length: 5 }, (_, i) => (
                  <Star
                    key={`${cat.label}-star-${i}`}
                    className={`h-3 w-3 ${
                      i < Math.round(cat.value!)
                        ? 'text-amber-400 fill-amber-400'
                        : 'text-slate-300 dark:text-slate-600'
                    }`}
                  />
                ))}
              </div>
              <span className="text-[11px] font-medium text-content dark:text-content-dark">
                {cat.value!.toFixed(1)}
              </span>
            </div>
          ))}
        </div>
      )}

      {/* Awards */}
      {awards.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {(awards as Array<Record<string, unknown>>).slice(0, 4).map((award, index) => (
            <span
              key={`award-${index}`}
              className="inline-flex items-center rounded-full bg-amber-50 dark:bg-amber-500/10 px-2.5 py-0.5 text-[11px] font-medium text-amber-700 dark:text-amber-300"
            >
              🏆 {getString(award, 'name') ?? 'Award'}
            </span>
          ))}
        </div>
      )}

      {/* Review snippets */}
      {reviews.length > 0 && (
        <div className="space-y-2">
          {reviews.map((review, index) => (
            <div
              key={`review-${index}`}
              className="rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <div className="flex items-center gap-2 mb-1">
                <span className="text-[11px] font-medium text-content dark:text-content-dark">
                  {review.userName ?? 'Anonymous'}
                </span>
                {review.overallRating != null && (
                  <span className="text-[11px] text-amber-600 dark:text-amber-400">
                    {formatRating(review.overallRating)}
                  </span>
                )}
                {review.dinedAt && (
                  <span className="text-[11px] text-content-secondary dark:text-content-secondary-dark">
                    · Dined {review.dinedAt}
                  </span>
                )}
              </div>
              {review.content && (
                <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark leading-relaxed">
                  "{truncateText(review.content, 200)}"
                </p>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
