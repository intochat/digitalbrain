import { useState } from 'react';
import { ProfileLayout } from './ProfileLayout';
import { ProfileMain } from './ProfileMain';

export const Profile = () => {
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);

  return (
    <ProfileLayout hasUnsavedChanges={hasUnsavedChanges}>
      <ProfileMain onUnsavedChanges={setHasUnsavedChanges} />
    </ProfileLayout>
  );
};
