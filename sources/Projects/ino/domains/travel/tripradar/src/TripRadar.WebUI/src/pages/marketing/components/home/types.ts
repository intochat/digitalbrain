/**
 * TypeScript interfaces for the Hero Section component
 * Simplified to support minimalist design without CTA buttons or feature highlights
 */

/**
 * Props interface for the HeroSection component
 * Simplified to remove CTA-related props as per requirements
 */
export interface HeroSectionProps {
  /** Optional CSS class name for styling customization */
  className?: string;
}

/**
 * Content structure interface for hero section messaging
 * Contains only essential content: headline and description
 */
export interface HeroContent {
  /** Main headline text that communicates the value proposition */
  headline: string;
  /** Supporting description text that explains the benefits */
  description: string;
}
