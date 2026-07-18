import { useState } from 'react';
import { Play } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

const DEMO_VIDEO_URL = 'https://www.youtube.com/embed/dQw4w9WgXcQ?autoplay=1';

export const DemoSection = () => {
  const { t } = useFrontendLanguage();
  const [isPlaying, setIsPlaying] = useState(false);

  return (
    <section className="min-h-screen flex items-center justify-center bg-surface dark:bg-surface-dark px-4 sm:px-6 lg:px-8">
      <div className="w-full max-w-3xl mx-auto">
        <div className="relative aspect-video rounded-xl overflow-hidden border border-outline dark:border-outline-dark bg-content dark:bg-content-dark">
          {isPlaying ? (
            <iframe
              src={DEMO_VIDEO_URL}
              title={t('TripRadar product demo')}
              className="absolute inset-0 w-full h-full"
              allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
              allowFullScreen
            />
          ) : (
            <button
              type="button"
              onClick={() => setIsPlaying(true)}
              className="absolute inset-0 w-full h-full flex items-center justify-center group cursor-pointer"
              aria-label={t('Play demo video')}
            >
              <div className="flex h-16 w-16 items-center justify-center rounded-full bg-surface/90 dark:bg-surface-dark/90 transition-colors group-hover:bg-surface-accent dark:group-hover:bg-surface-accent-dark">
                <Play className="h-6 w-6 text-content dark:text-content-dark ml-1" aria-hidden="true" />
              </div>
            </button>
          )}
        </div>
      </div>
    </section>
  );
};
