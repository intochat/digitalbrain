import { CrownIcon } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useSubscriptionQuery } from 'entities/payment/api';
import { usePrivacyModeQuery, useUpdatePrivacyModeMutation } from 'entities/preferences/api';
import { useProfileQuery, useUpdateProfileMutation } from 'entities/user/api';
import { Switch } from 'shared/ui';

interface ToggleRowProps {
  title: string;
  description: string;
  enabled: boolean;
  disabled: boolean;
  loading?: boolean;
  onToggle: () => void;
  badge?: React.ReactNode;
  hint?: string;
}

const ToggleRow = ({ title, description, enabled, disabled, loading, onToggle, badge, hint }: ToggleRowProps) => (
  <div className="flex items-center justify-between gap-4 py-3">
    <div className="min-w-0 flex-1">
      <div className="flex items-center gap-2">
        <span className="text-sm text-content dark:text-content-dark">{title}</span>
        {badge}
      </div>
      <p className="text-xs text-content-muted dark:text-content-muted-dark mt-0.5">{description}</p>
      {hint && <p className="text-[11px] text-amber-600 dark:text-amber-400 mt-1">{hint}</p>}
    </div>
    <Switch checked={enabled} onChange={onToggle} disabled={disabled} loading={loading} />
  </div>
);

export const PrivacySection = () => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const { data: profile } = useProfileQuery();
  const { data: privacyMode, isLoading: isPrivacyModeLoading } = usePrivacyModeQuery();
  const updateProfileMutation = useUpdateProfileMutation();
  const updatePrivacyModeMutation = useUpdatePrivacyModeMutation();
  const subscriptionQuery = useSubscriptionQuery();
  const isPro =
    !subscriptionQuery.isLoading &&
    !subscriptionQuery.isError &&
    Boolean(subscriptionQuery.data) &&
    subscriptionQuery.data?.tierType?.toLowerCase() !== 'basic';
  const noTraceModeEnabled = isPro ? (privacyMode?.enabled ?? false) : false;

  const handleMarketingToggle = async () => {
    try {
      await updateProfileMutation.mutateAsync({ allowsMarketingEmails: !(profile?.allowsMarketingEmails || false) });
      showSuccess(t('Success'), t('Privacy settings updated successfully'));
    } catch {
      showError(t('Error'), t('Failed to update privacy settings. Please try again.'));
    }
  };

  const handleNoTraceToggle = async () => {
    if (!isPro) return;
    try {
      await updatePrivacyModeMutation.mutateAsync(!noTraceModeEnabled);
      showSuccess(t('Success'), t('Privacy settings updated successfully'));
    } catch {
      showError(t('Error'), t('Failed to update privacy settings. Please try again.'));
    }
  };

  return (
    <section>
      <h3 className="text-sm font-semibold text-content dark:text-content-dark mb-1">{t('Privacy')}</h3>
      <div className="divide-y divide-outline/40 dark:divide-outline-dark/40">
        <ToggleRow
          title={t('Marketing Emails')}
          description={t('Receive updates about new features, travel tips, and special offers')}
          enabled={profile?.allowsMarketingEmails || false}
          disabled={updateProfileMutation.isPending}
          loading={updateProfileMutation.isPending}
          onToggle={handleMarketingToggle}
        />
        <ToggleRow
          title={t('No-trace mode')}
          description={t('Send requests in privacy mode and skip trip history saving')}
          enabled={noTraceModeEnabled}
          disabled={!isPro || isPrivacyModeLoading || updatePrivacyModeMutation.isPending}
          loading={updatePrivacyModeMutation.isPending}
          onToggle={handleNoTraceToggle}
          badge={
            !isPro ? (
              <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-full text-[10px] font-medium bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-400">
                <CrownIcon className="h-2.5 w-2.5" />
                Pro
              </span>
            ) : undefined
          }
          hint={!isPro ? t('Upgrade to a paid plan to enable No-trace mode') : undefined}
        />
      </div>
    </section>
  );
};
