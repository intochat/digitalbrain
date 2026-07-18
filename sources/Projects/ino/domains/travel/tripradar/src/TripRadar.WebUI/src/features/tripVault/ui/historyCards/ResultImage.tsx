import { useState } from 'react';
import { Building2, MapPin, Plane, Calendar, UtensilsCrossed } from 'lucide-react';

type ResultImageVariant = 'hotel' | 'flight' | 'event' | 'place' | 'restaurant' | 'generic';

interface ResultImageProps {
  src: string | null | undefined;
  alt: string;
  variant?: ResultImageVariant;
  className?: string;
}

const ICON_BY_VARIANT: Record<ResultImageVariant, typeof Building2> = {
  hotel: Building2,
  flight: Plane,
  event: Calendar,
  place: MapPin,
  restaurant: UtensilsCrossed,
  generic: MapPin,
};

export const ResultImage = ({ src, alt, variant = 'generic', className = 'h-10 w-10' }: ResultImageProps) => {
  const [failed, setFailed] = useState(false);

  if (!src || failed) {
    const Icon = ICON_BY_VARIANT[variant];
    return (
      <div
        className={`${className} rounded-lg bg-surface-accent dark:bg-surface-accent-dark flex items-center justify-center flex-shrink-0`}
      >
        <Icon className="h-4 w-4 text-content-muted dark:text-content-muted-dark" />
      </div>
    );
  }

  return (
    <img
      src={src}
      alt={alt}
      className={`${className} rounded-lg object-cover flex-shrink-0`}
      onError={() => setFailed(true)}
    />
  );
};
