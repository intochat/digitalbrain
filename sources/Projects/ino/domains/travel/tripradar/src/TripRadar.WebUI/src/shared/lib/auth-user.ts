import type { User } from 'app/types';
import type { GetUserProfileResponse } from 'shared/api';

export const resolveSubscriptionFromTierName = (tierName?: string | null): User['subscription'] => {
  const normalizedTier = (tierName || '').trim().toLowerCase();

  if (normalizedTier === 'advanced' || normalizedTier === 'enterprise') {
    return 'enterprise';
  }

  if (normalizedTier === 'essential' || normalizedTier === 'premium') {
    return 'premium';
  }

  return 'free';
};

export const mapProfileToAuthUser = (profile: GetUserProfileResponse): User => {
  const displayName =
    [profile.firstName, profile.lastName]
      .filter((part): part is string => !!part && part.trim().length > 0)
      .join(' ') || profile.username;

  const avatarName = displayName || profile.username;

  return {
    username: profile.username,
    name: displayName,
    email: profile.email,
    avatar:
      profile.profilePictureUrl ||
      `https://ui-avatars.com/api/?name=${encodeURIComponent(avatarName)}&background=6366f1&color=fff`,
    subscription: resolveSubscriptionFromTierName(profile.tierName),
  };
};
