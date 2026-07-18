import { useEffect, useMemo, useState } from 'react';
import { Smartphone } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useSubscriptionQuery } from 'entities/payment/api';
import { usePortalTimezonesQuery } from 'entities/portal';
import { TelegramConnect } from 'features/auth/ui/TelegramConnect';
import type { GetUserProfileResponse, UpdateUserProfileRequest } from 'shared/api';
import type { LinkTelegramResponse } from 'shared/api/types';
import { ROUTES } from 'shared/config/routes';
import { useAuthStore } from 'shared/store/auth';
import { Dropdown } from 'shared/ui';
import { buildTimezoneOptions } from '../lib/timezoneOptions';
import { InlineEditor } from './InlineEditor';
import { NameInlineEditor } from './NameInlineEditor';

interface ProfileInfoDisplayProps {
  profile: GetUserProfileResponse;
  onUpdateProfile: (data: UpdateUserProfileRequest) => Promise<boolean>;
  isUpdating?: boolean;
  onUnsavedChanges?: (hasChanges: boolean) => void;
}

export const ProfileInfoDisplay = ({
  profile,
  onUpdateProfile,
  isUpdating = false,
  onUnsavedChanges,
}: ProfileInfoDisplayProps) => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const { showSuccess } = useToast();
  const { user, login } = useAuthStore();
  const [isTelegramSyncOpen, setIsTelegramSyncOpen] = useState(false);
  const [telegramSyncError, setTelegramSyncError] = useState<string | null>(null);
  const { data: timezonesResponse, isLoading: isTimezonesLoading } = usePortalTimezonesQuery();
  const subscriptionQuery = useSubscriptionQuery();
  const currentTierName = subscriptionQuery.data?.tierType || profile.tierName;
  const normalizedTierName = currentTierName.charAt(0).toUpperCase() + currentTierName.slice(1).toLowerCase();
  const localizedTierName = t(normalizedTierName);
  const isBasicTier = currentTierName.toLowerCase() === 'basic';
  const [timezoneDisplayDate, setTimezoneDisplayDate] = useState(() => new Date());
  const timezoneOptions = useMemo(
    () => buildTimezoneOptions(timezonesResponse?.timezones, t, timezoneDisplayDate),
    [timezonesResponse?.timezones, t, timezoneDisplayDate]
  );
  const selectedTimezoneId = useMemo(() => {
    const currentTimezoneId = profile.timezoneId ?? 1;
    if (timezoneOptions.length === 0) return currentTimezoneId;
    return timezoneOptions.some(option => option.value === currentTimezoneId)
      ? currentTimezoneId
      : (timezoneOptions[0]?.value ?? currentTimezoneId);
  }, [profile.timezoneId, timezoneOptions]);
  const [editingStates, setEditingStates] = useState({ name: false, phone: false });

  useEffect(() => {
    onUnsavedChanges?.(Object.values(editingStates).some(Boolean));
  }, [editingStates, onUnsavedChanges]);

  useEffect(() => {
    const timerId = window.setInterval(() => setTimezoneDisplayDate(new Date()), 60_000);
    return () => window.clearInterval(timerId);
  }, []);

  const handleEditingChange = (field: keyof typeof editingStates, isEditing: boolean) => {
    setEditingStates(prev => ({ ...prev, [field]: isEditing }));
  };

  const handleNameSave = async (firstName: string, lastName: string) => {
    await onUpdateProfile({ firstName, lastName });
  };

  const handlePhoneSave = async (phoneNumber: string) => {
    await onUpdateProfile({ phoneNumber });
  };

  const handleTimezoneChange = (timezoneId: number) => {
    void onUpdateProfile({ timezoneId });
  };

  const handleTelegramSyncSuccess = (response: LinkTelegramResponse) => {
    if (!response.username) {
      setTelegramSyncError(t('Username not received from server. Please try again.'));
      return;
    }

    login({
      username: response.username,
      name: response.username,
      email: response.email,
      avatar:
        user?.avatar ||
        `https://ui-avatars.com/api/?name=${encodeURIComponent(response.username)}&background=6366f1&color=fff`,
      subscription: user?.subscription ?? 'free',
    });

    setTelegramSyncError(null);
    setIsTelegramSyncOpen(false);
    showSuccess(t('Telegram synced'), t('Telegram username has been updated.'));
  };

  return (
    <div className="space-y-8">
      {/* Personal Information */}
      <section>
        <SectionHeader>{t('Personal Information')}</SectionHeader>
        <div className="space-y-0.5">
          <div className="py-2.5 border-b border-outline/30 dark:border-outline-dark/30">
            <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark mb-0.5">
              {t('Username')}
            </label>
            <div className="flex items-center gap-2">
              <span className="text-sm text-content dark:text-content-dark">{profile.username}</span>
              <button
                type="button"
                onClick={() => {
                  setTelegramSyncError(null);
                  setIsTelegramSyncOpen(prev => !prev);
                }}
                className={`inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium transition-colors ${
                  isTelegramSyncOpen
                    ? 'bg-surface-accent dark:bg-surface-accent-dark text-content dark:text-content-dark'
                    : 'text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark'
                }`}
                title={t('Sync Telegram username')}
              >
                <Smartphone className="h-3 w-3" />
                {t('Sync')}
              </button>
            </div>
            {isTelegramSyncOpen && (
              <div className="mt-3">
                <TelegramConnect
                  mode="usernameSync"
                  onSuccess={handleTelegramSyncSuccess}
                  onError={error => setTelegramSyncError(error)}
                />
                {telegramSyncError && (
                  <p className="mt-2 text-xs text-red-600 dark:text-red-400" role="alert">
                    {telegramSyncError}
                  </p>
                )}
              </div>
            )}
          </div>

          <div className="py-2.5 border-b border-outline/30 dark:border-outline-dark/30">
            <NameInlineEditor
              firstName={profile.firstName || ''}
              lastName={profile.lastName || ''}
              onSave={handleNameSave}
              isLoading={isUpdating}
              onEditingChange={isEditing => handleEditingChange('name', isEditing)}
            />
          </div>

          <ProfileRow label={t('Email Address')} border>
            <div className="flex items-center gap-2">
              <span className="text-sm text-content dark:text-content-dark">{profile.email}</span>
              <StatusBadge
                variant={profile.isEmailConfirmed ? 'success' : 'warning'}
                label={profile.isEmailConfirmed ? t('Verified') : t('Unverified')}
              />
            </div>
          </ProfileRow>

          <div className="py-2.5">
            <InlineEditor
              value={profile.phoneNumber || ''}
              onSave={handlePhoneSave}
              placeholder={t('Add phone number')}
              label={t('Phone Number')}
              isLoading={isUpdating}
              type="tel"
              required={false}
              onEditingChange={isEditing => handleEditingChange('phone', isEditing)}
            />
          </div>
        </div>
      </section>

      {/* Preferences */}
      <section>
        <SectionHeader>{t('Preferences')}</SectionHeader>
        <div className="space-y-0.5">
          <div className="py-2.5 border-b border-outline/30 dark:border-outline-dark/30">
            <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark mb-1.5">
              {t('profile.preferences.timezone.title')}
            </label>
            <div className="max-w-full sm:max-w-md">
              <Dropdown
                value={selectedTimezoneId}
                options={timezoneOptions}
                onChange={handleTimezoneChange}
                disabled={isUpdating || isTimezonesLoading || timezoneOptions.length === 0}
                placeholder={t('Loading timezones...')}
                searchable
                searchPlaceholder={t('Search timezone...')}
                aria-label={t('profile.preferences.timezone.title')}
              />
            </div>
          </div>
        </div>
      </section>

      {/* Subscription */}
      <section>
        <SectionHeader>{t('Subscription Plan')}</SectionHeader>
        <div className="flex items-center justify-between py-1">
          <div className="flex items-center gap-2">
            <span className="text-sm text-content dark:text-content-dark font-medium capitalize">
              {localizedTierName}
            </span>
            {!isBasicTier && <StatusBadge variant="info" label="Pro" />}
          </div>
          {isBasicTier && (
            <button
              onClick={() => navigate(ROUTES.PRICING)}
              className="px-3 py-1.5 text-xs font-medium rounded-lg bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors"
            >
              {t('Upgrade')}
            </button>
          )}
        </div>
      </section>
    </div>
  );
};

const SectionHeader = ({ children }: { children: React.ReactNode }) => (
  <h3 className="text-xs font-medium uppercase tracking-wider text-content-muted dark:text-content-muted-dark mb-3">
    {children}
  </h3>
);

interface ProfileRowProps {
  label: string;
  children: React.ReactNode;
  border?: boolean;
}

const ProfileRow = ({ label, children, border = false }: ProfileRowProps) => (
  <div className={`py-2.5 ${border ? 'border-b border-outline/30 dark:border-outline-dark/30' : ''}`}>
    <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark mb-0.5">
      {label}
    </label>
    {children}
  </div>
);

const BADGE_VARIANTS = {
  success: 'bg-green-100 dark:bg-green-500/15 text-green-700 dark:text-green-400',
  warning: 'bg-yellow-100 dark:bg-yellow-500/15 text-yellow-700 dark:text-yellow-400',
  info: 'bg-blue-100 dark:bg-blue-500/15 text-blue-700 dark:text-blue-400',
} as const;

interface StatusBadgeProps {
  variant: keyof typeof BADGE_VARIANTS;
  label: string;
}

const StatusBadge = ({ variant, label }: StatusBadgeProps) => (
  <span className={`px-1.5 py-0.5 text-[11px] font-medium rounded ${BADGE_VARIANTS[variant]}`}>{label}</span>
);
