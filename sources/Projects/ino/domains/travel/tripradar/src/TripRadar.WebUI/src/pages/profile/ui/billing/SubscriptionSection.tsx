import { ArrowRight } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { ROUTES } from 'shared/config/routes';
import { Button, SectionError, Switch } from 'shared/ui';
import { capitalize, formatDate, formatPrice } from './billingUtils';
import { SubscriptionSkeleton } from './SubscriptionSkeleton';
import { useAutoRenewal } from './useAutoRenewal';
import { usePayAsYouGo } from './usePayAsYouGo';
import { useTierInfo } from './useTierInfo';

const StatusDot = ({ status }: { status: string }) => {
  const dotColor: Record<string, string> = {
    active: 'bg-emerald-500',
    trialing: 'bg-amber-400',
    paused: 'bg-amber-400',
    past_due: 'bg-red-400',
    canceled: 'bg-content-muted dark:bg-content-muted-dark',
    incomplete: 'bg-red-400',
    unpaid: 'bg-red-400',
  };
  return (
    <span
      className={`inline-block h-2 w-2 rounded-full ${dotColor[status] || 'bg-content-muted dark:bg-content-muted-dark'}`}
    />
  );
};

export const SubscriptionSection = ({ onCancelSubscription }: { onCancelSubscription: () => void }) => {
  const { t, language } = useFrontendLanguage();
  const navigate = useNavigate();
  const { isBasicTier, localizedTierName, subscription, isLoading, error, refetch } = useTierInfo();
  const { isAutoRenewalOn, handleToggle, isPending } = useAutoRenewal({ subscription });
  const { isPayAsYouGoOn, handleToggle: handlePayAsYouGoToggle, isPending: isPayAsYouGoPending } = usePayAsYouGo();

  if (isLoading) {
    return <SubscriptionSkeleton />;
  }

  if (error) {
    return <SectionError message={t('Failed to load subscription data')} onRetry={() => refetch()} />;
  }

  return (
    <div>
      <div className="flex items-center gap-2 mb-1">
        {subscription?.status && <StatusDot status={subscription.status} />}
        <h3 className="text-sm font-medium text-content-secondary dark:text-content-secondary-dark">
          {t('{tierName} Plan', { tierName: localizedTierName })}
          {subscription?.status && subscription.status !== 'incomplete' && (
            <span className="ml-1.5 text-xs font-normal text-content-muted dark:text-content-muted-dark">
              · {t(capitalize(subscription.status))}
            </span>
          )}
        </h3>
      </div>

      <div className="mb-3">
        {subscription?.priceAmount != null ? (
          <p className="text-xl font-semibold text-content dark:text-content-dark">
            {formatPrice(subscription.priceAmount, subscription.currency, language)}
            {subscription.billingPeriod && (
              <span className="text-sm font-normal text-content-muted dark:text-content-muted-dark ml-0.5">
                / {t(subscription.billingPeriod)}
              </span>
            )}
          </p>
        ) : isBasicTier ? (
          <p className="text-xl font-semibold text-content dark:text-content-dark">{t('Free')}</p>
        ) : null}
      </div>

      <div className="space-y-0.5 mb-5">
        <p className="text-xs text-content-muted dark:text-content-muted-dark">
          {subscription?.nextInvoiceDate
            ? t('Next billing date: {date}', { date: formatDate(subscription.nextInvoiceDate, language) })
            : '\u00A0'}
        </p>
        {subscription?.discountPercent != null && subscription.discountPercent > 0 && (
          <p className="text-xs text-content-muted dark:text-content-muted-dark">
            {t('{percent}% discount applied', { percent: subscription.discountPercent })}
          </p>
        )}
        {subscription?.pendingTierType != null && (
          <p className="text-xs text-content-muted dark:text-content-muted-dark">
            {t('Switching to {plan}', { plan: t(capitalize(subscription.pendingTierType)) })}
            {subscription.pendingTierEffectiveDate &&
              ` · ${formatDate(subscription.pendingTierEffectiveDate, language)}`}
          </p>
        )}
      </div>

      {!isBasicTier && (
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
            <label className="inline-flex items-center gap-2 cursor-pointer">
              <Switch
                checked={isAutoRenewalOn}
                onChange={handleToggle}
                loading={isPending}
                aria-label={t('Auto-renewal')}
              />
              <span className="text-sm text-content dark:text-content-dark">{t('Auto-renewal')}</span>
            </label>
            <label className="inline-flex items-center gap-2 cursor-pointer">
              <Switch
                checked={isPayAsYouGoOn}
                onChange={handlePayAsYouGoToggle}
                loading={isPayAsYouGoPending}
                aria-label={t('Toggle pay as you go')}
              />
              <span className="text-sm text-content dark:text-content-dark">{t('Pay as you go')}</span>
            </label>
          </div>

          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" size="sm" onClick={() => navigate(ROUTES.PRICING + '?from=billing')}>
              {t('Change Plan')}
            </Button>
            <Button variant="ghost" size="sm" onClick={onCancelSubscription}>
              {t('Cancel Subscription')}
            </Button>
          </div>
        </div>
      )}

      {isBasicTier && (
        <div className="flex flex-col gap-3">
          <p className="text-xs text-content-muted dark:text-content-muted-dark">
            {t('Higher limits, priority support, advanced features, and export capabilities.')}
          </p>
          <Button size="sm" className="self-start gap-1.5 px-3 py-1.5" onClick={() => navigate(ROUTES.PRICING)}>
            {t('Upgrade Plan')}
            <ArrowRight className="h-3.5 w-3.5" />
          </Button>
        </div>
      )}
    </div>
  );
};
