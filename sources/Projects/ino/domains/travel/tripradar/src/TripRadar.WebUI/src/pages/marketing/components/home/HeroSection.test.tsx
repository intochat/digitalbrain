import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { HeroSection } from './HeroSection';

describe('HeroSection', () => {
  it('renders headline and description', () => {
    render(<HeroSection />);

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Save money on every trip: find the best options in seconds'
    );
    expect(
      screen.getByText('TripRadar compares routes and prices fast, so you can book with lower total cost.')
    ).toBeInTheDocument();
  });

  it('keeps semantic structure and aria references', () => {
    render(<HeroSection />);

    const banner = screen.getByRole('banner');
    const headline = screen.getByRole('heading', { level: 1 });
    const description = screen.getByText(/TripRadar compares routes and prices fast/i);

    expect(banner).toHaveAttribute('aria-label', "Hero section introducing TripRadar's travel planning platform");
    expect(headline).toHaveAttribute('id', 'hero-headline');
    expect(description).toHaveAttribute('id', 'hero-description');
    expect(description).toHaveAttribute('aria-describedby', 'hero-headline');
  });

  it('has no CTA buttons or links', () => {
    render(<HeroSection />);

    expect(screen.queryByRole('button')).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
  });
});
