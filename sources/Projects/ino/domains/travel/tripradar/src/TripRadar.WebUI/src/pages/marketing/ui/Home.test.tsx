import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { Home } from './Home';

vi.mock('../components/home', () => ({
  HeroSection: () => <div data-testid="hero-section">Hero Section</div>,
}));

const renderHome = () => {
  return render(
    <BrowserRouter>
      <Home />
    </BrowserRouter>
  );
};

describe('Home Page', () => {
  beforeEach(() => {
    window.location.hash = '';
  });

  it('renders hero section', () => {
    renderHome();

    expect(screen.getByTestId('hero-section')).toBeInTheDocument();

    const sections = screen.getAllByRole('main')[0].children;
    expect(sections[0]).toHaveAttribute('id', 'hero');
  });

  it('has proper semantic structure', () => {
    renderHome();

    const skipLink = screen.getByText('Skip to main content');
    expect(skipLink).toHaveAttribute('href', '#main-content');

    const mainContent = screen.getByRole('main');
    expect(mainContent).toHaveAttribute('id', 'main-content');
    expect(mainContent).toHaveAttribute('aria-label', 'TripRadar home page');

    const liveRegion = document.getElementById('live-region');
    expect(liveRegion).toHaveAttribute('aria-live', 'polite');
  });

  it('has hero section landmark', () => {
    renderHome();

    const heroSection = document.getElementById('hero');
    expect(heroSection).toHaveAttribute('aria-labelledby', 'hero-heading');
  });
});
