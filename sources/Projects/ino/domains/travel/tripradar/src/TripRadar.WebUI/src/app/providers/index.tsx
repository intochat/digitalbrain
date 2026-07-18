import { ReactNode, useCallback, useEffect } from 'react';
import { BrowserRouter as Router, useLocation } from 'react-router-dom';
import { profileApi } from 'entities/user/api';
import { trackPageView } from 'shared/lib';
import { useAuthStore } from 'shared/store/auth';
import { LoadingSpinner } from 'shared/ui';
import { useFrontendLanguage } from './FrontendLanguageContext';
import { FrontendLanguageProvider } from './FrontendLanguageProvider';
import { QueryProvider } from './QueryProvider';
import { ThemeProvider } from './ThemeContext';
import { ToastProvider } from './ToastProvider';

const basename = import.meta.env.BASE_URL;
const defaultCanonicalOrigin = 'https://tripradar.io';
const defaultOgImage = 'https://tripradar.io/tripradar-logo-brand.png';

type TwitterCardType = 'summary' | 'summary_large_image';

interface RouteMetadata {
  title: string;
  description: string;
  canonicalPath?: string;
  ogTitle?: string;
  ogDescription?: string;
  ogImage?: string;
  twitterCard?: TwitterCardType;
}

const defaultRouteMetadata: RouteMetadata = {
  title: 'TripRadar - AI Trip Planning in Telegram',
  description:
    'Plan trips faster, keep routes and budgets in one place, and launch personalized travel plans directly from Telegram with TripRadar.',
  canonicalPath: '/',
  ogImage: defaultOgImage,
  twitterCard: 'summary_large_image',
};

const routeMetadataByPath: Record<string, RouteMetadata> = {
  '/': defaultRouteMetadata,
  '/pricing': {
    title: 'TripRadar Pricing - Choose Your Travel Planning Plan',
    description:
      'Compare TripRadar plans, choose monthly or yearly billing, and pick the best option for budget-friendly AI trip planning.',
    canonicalPath: '/pricing',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/changelog': {
    title: 'TripRadar Changelog - Product Updates and Release Notes',
    description:
      'Read the latest TripRadar product updates, release notes, improvements, and fixes in one public timeline.',
    canonicalPath: '/changelog',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/help': {
    title: 'TripRadar Help Center - Getting Started, Billing, Support',
    description: 'Get onboarding help, account support, billing answers, and troubleshooting steps for TripRadar.',
    canonicalPath: '/help',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/feedback': {
    title: 'TripRadar Feedback - Report Issues and Share Requests',
    description: 'Share feature requests, report issues, and help improve TripRadar travel planning workflows.',
    canonicalPath: '/feedback',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/privacy': {
    title: 'TripRadar Privacy Policy',
    description: 'Read how TripRadar processes personal data, privacy rights, retention windows, and consent controls.',
    canonicalPath: '/privacy',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/terms': {
    title: 'TripRadar Terms of Service',
    description: 'Review TripRadar terms, account responsibilities, billing rules, and legal policies.',
    canonicalPath: '/terms',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/cookies': {
    title: 'TripRadar Cookie Policy',
    description: 'Understand TripRadar cookie categories, consent controls, and tracking technology usage.',
    canonicalPath: '/cookies',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/signin': {
    title: 'Sign In - TripRadar',
    description: 'Sign in to TripRadar and continue planning, tracking, and optimizing your trips.',
    canonicalPath: '/signin',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/signup': {
    title: 'Create Account - TripRadar',
    description: 'Create your TripRadar account and start planning personalized trips in minutes.',
    canonicalPath: '/signup',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/telegram-trip-planner': {
    title: 'Telegram Trip Planner - TripRadar Guide',
    description: 'Learn how to plan repeat trips from Telegram with faster itinerary and budget decisions.',
    canonicalPath: '/telegram-trip-planner',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/ai-trip-planner-budget': {
    title: 'AI Trip Planner for Budget Travelers - TripRadar Guide',
    description: 'A practical guide for budget-first trip planning with faster route and spend checks.',
    canonicalPath: '/ai-trip-planner-budget',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/trip-planning-assistant-alternatives': {
    title: 'Trip Planning Assistant Alternatives - TripRadar Guide',
    description: 'Compare manual planning stacks and assistant workflows for repeat travel.',
    canonicalPath: '/trip-planning-assistant-alternatives',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/trip-budget-guide-2026': {
    title: 'How to Plan a Trip Budget in 2026 - TripRadar Guide',
    description: 'Use a constraint-first checklist to plan trip budgets with fewer surprises.',
    canonicalPath: '/trip-budget-guide-2026',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/trip-checklist-template': {
    title: 'Trip Checklist Template - TripRadar Guide',
    description: 'Use a reusable checklist template to speed up planning and keep trip budgets on track.',
    canonicalPath: '/trip-checklist-template',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/manual-planning-vs-tripradar': {
    title: 'Manual Planning vs TripRadar - TripRadar Guide',
    description: 'See when manual planning works and when a centralized TripRadar workflow is better.',
    canonicalPath: '/manual-planning-vs-tripradar',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/example-trip-plan': {
    title: 'Example Trip Plan - TripRadar Guide',
    description: 'Review a sample Telegram-native trip plan and budget flow before starting.',
    canonicalPath: '/example-trip-plan',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
  '/savings-methodology': {
    title: 'TripRadar Savings Methodology',
    description: 'See how TripRadar calculates planning-time and budget savings estimates.',
    canonicalPath: '/savings-methodology',
    ogImage: defaultOgImage,
    twitterCard: 'summary_large_image',
  },
};

interface ProvidersProps {
  children: ReactNode;
}

const resolveCanonicalOrigin = (): string => {
  if (typeof window === 'undefined') {
    return defaultCanonicalOrigin;
  }

  const origin = window.location.origin;
  if (!origin || origin.includes('localhost') || origin.includes('127.0.0.1')) {
    return defaultCanonicalOrigin;
  }

  return origin;
};

const ensureMetaTag = (attribute: 'name' | 'property', value: string): HTMLMetaElement => {
  const selector = `meta[${attribute}="${value}"]`;
  let metaTag = document.head.querySelector(selector) as HTMLMetaElement | null;
  if (!metaTag) {
    metaTag = document.createElement('meta');
    metaTag.setAttribute(attribute, value);
    document.head.appendChild(metaTag);
  }

  return metaTag;
};

const ensureCanonicalLink = (): HTMLLinkElement => {
  let canonicalLink = document.head.querySelector('link[rel="canonical"]') as HTMLLinkElement | null;
  if (!canonicalLink) {
    canonicalLink = document.createElement('link');
    canonicalLink.setAttribute('rel', 'canonical');
    document.head.appendChild(canonicalLink);
  }

  return canonicalLink;
};

const AuthInitializer = ({ children }: { children: ReactNode }) => {
  const { initializeAuth, isLoading } = useAuthStore();

  const fetchProfile = useCallback(() => profileApi.getProfile({ skipUnauthorizedRedirect: true }), []);

  useEffect(() => {
    initializeAuth(fetchProfile);
  }, [initializeAuth, fetchProfile]);

  if (isLoading) {
    return <LoadingSpinner size="lg" fullScreen />;
  }

  return <>{children}</>;
};

const TelemetryRouteTracker = () => {
  const location = useLocation();

  useEffect(() => {
    trackPageView(`${location.pathname}${location.search}${location.hash}`);
  }, [location.hash, location.pathname, location.search]);

  return null;
};

const RouteMetadataTracker = () => {
  const location = useLocation();

  useEffect(() => {
    const metadata = routeMetadataByPath[location.pathname] ?? defaultRouteMetadata;
    const canonicalOrigin = resolveCanonicalOrigin();
    const canonicalPath = metadata.canonicalPath ?? location.pathname;
    const normalizedPath = canonicalPath === '/' ? '' : canonicalPath;
    const canonicalUrl = `${canonicalOrigin}${normalizedPath}`;
    const ogTitle = metadata.ogTitle ?? metadata.title;
    const ogDescription = metadata.ogDescription ?? metadata.description;
    const ogImage = metadata.ogImage ?? defaultOgImage;
    const twitterCard = metadata.twitterCard ?? 'summary_large_image';

    document.title = metadata.title;
    ensureMetaTag('name', 'description').setAttribute('content', metadata.description);
    ensureMetaTag('property', 'og:title').setAttribute('content', ogTitle);
    ensureMetaTag('property', 'og:description').setAttribute('content', ogDescription);
    ensureMetaTag('property', 'og:type').setAttribute('content', 'website');
    ensureMetaTag('property', 'og:url').setAttribute('content', canonicalUrl);
    ensureMetaTag('property', 'og:image').setAttribute('content', ogImage);
    ensureMetaTag('name', 'twitter:card').setAttribute('content', twitterCard);
    ensureMetaTag('name', 'twitter:title').setAttribute('content', ogTitle);
    ensureMetaTag('name', 'twitter:description').setAttribute('content', ogDescription);
    ensureMetaTag('name', 'twitter:image').setAttribute('content', ogImage);
    ensureCanonicalLink().setAttribute('href', canonicalUrl);
  }, [location.pathname]);

  return null;
};

export const Providers = ({ children }: ProvidersProps) => {
  return (
    <QueryProvider>
      <ThemeProvider>
        <ToastProvider>
          <Router basename={basename}>
            <TelemetryRouteTracker />
            <RouteMetadataTracker />
            <AuthInitializer>
              <FrontendLanguageProvider>{children}</FrontendLanguageProvider>
            </AuthInitializer>
          </Router>
        </ToastProvider>
      </ThemeProvider>
    </QueryProvider>
  );
};

export { useFrontendLanguage } from './FrontendLanguageContext';
