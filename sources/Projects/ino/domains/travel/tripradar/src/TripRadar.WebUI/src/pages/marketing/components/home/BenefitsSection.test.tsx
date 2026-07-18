import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { BenefitsSection } from './BenefitsSection';

describe('BenefitsSection', () => {
  it('renders section heading and description', () => {
    render(<BenefitsSection />);

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Why choose TripRadar?');
    expect(screen.getByText(/Everything you need to plan/)).toBeInTheDocument();
  });

  it('renders all 4 benefit cards', () => {
    render(<BenefitsSection />);

    const cards = screen.getAllByRole('listitem');
    expect(cards).toHaveLength(4);

    expect(screen.getByText('AI-Powered Planning')).toBeInTheDocument();
    expect(screen.getByText('Instant Results')).toBeInTheDocument();
    expect(screen.getByText('Telegram Integration')).toBeInTheDocument();
    expect(screen.getByText('Budget Optimization')).toBeInTheDocument();
  });

  it('has proper semantic structure', () => {
    render(<BenefitsSection />);

    const section = document.querySelector('section');
    expect(section).toHaveAttribute('aria-labelledby', 'features-heading');

    const heading = screen.getByRole('heading', { level: 2 });
    expect(heading).toHaveAttribute('id', 'features-heading');

    const grid = screen.getByRole('list');
    expect(grid).toHaveAttribute('aria-label', 'TripRadar key features and benefits');
  });

  it('uses lucide icons instead of emoji', () => {
    render(<BenefitsSection />);

    const svgIcons = document.querySelectorAll('svg[aria-hidden="true"]');
    expect(svgIcons.length).toBeGreaterThanOrEqual(4);
  });

  it('uses design tokens for theming', () => {
    render(<BenefitsSection />);

    const section = document.querySelector('section');
    expect(section).toHaveClass('bg-surface', 'dark:bg-surface-dark');

    const heading = screen.getByRole('heading', { level: 2 });
    expect(heading).toHaveClass('text-content', 'dark:text-content-dark');
  });
});
