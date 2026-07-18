import { useState, useEffect } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useProfileQuery, useUpdateProfileMutation } from 'entities/user/api';
import type { UpdateUserProfileRequest } from 'shared/api';
import { ProfileInfoDisplay } from './ProfileInfoDisplay';

interface ProfileMainProps {
  onUnsavedChanges?: (hasChanges: boolean) => void;
}

export const ProfileMain = ({ onUnsavedChanges }: ProfileMainProps = {}) => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);

  const { data: profile, isLoading, error } = useProfileQuery();
  const updateProfileMutation = useUpdateProfileMutation();

  // Notify parent about unsaved changes
  useEffect(() => {
    onUnsavedChanges?.(hasUnsavedChanges);
  }, [hasUnsavedChanges, onUnsavedChanges]);

  const handleUpdateProfile = async (data: UpdateUserProfileRequest): Promise<boolean> => {
    try {
      await updateProfileMutation.mutateAsync(data);
      showSuccess(t('Profile updated'), t('Your changes have been saved successfully'));
      return true;
    } catch (error) {
      console.error('Profile update failed:', error);
      showError(t('Update failed'), t('Failed to save your changes. Please try again.'));
      return false;
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-48">
        <div className="text-center">
          <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-content-muted mx-auto mb-3" />
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">{t('Loading profile...')}</p>
        </div>
      </div>
    );
  }

  if (error || !profile) {
    return (
      <div className="flex items-center justify-center h-48">
        <div className="text-center">
          <p className="text-sm font-medium text-content dark:text-content-dark mb-1">{t('Failed to load profile')}</p>
          <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
            {t('Please refresh the page or try again later')}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div>
      <ProfileInfoDisplay
        profile={profile}
        onUpdateProfile={handleUpdateProfile}
        isUpdating={updateProfileMutation.isPending}
        onUnsavedChanges={setHasUnsavedChanges}
      />
    </div>
  );
};
