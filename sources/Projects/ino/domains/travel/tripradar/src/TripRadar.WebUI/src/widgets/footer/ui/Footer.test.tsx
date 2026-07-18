import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { Footer } from './Footer';

const renderFooter = (props = {}) => {
  return render(
    <BrowserRouter>
      <Footer {...props} />
    </BrowserRouter>
  );
};

describe('Footer', () => {
  it('should render minimal footer with essential elements only', () => {
    renderFooter();

    // Check that logo is NOT present (removed for minimalism)
    expect(screen.queryByText('TripRadar')).not.toBeInTheDocument();

    // Check that company tagline is NOT present
    expect(screen.queryByText('Travel planning made simple')).not.toBeInTheDocument();

    // Check that navigation links are NOT present
    expect(screen.queryByText('Navigation')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Home' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Pricing' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'About' })).not.toBeInTheDocument();

    // Check that social links section is NOT present
    expect(screen.queryByText('Connect')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Visit TripRadar on GitHub')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Follow TripRadar on X')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Connect with TripRadar on LinkedIn')).not.toBeInTheDocument();

    // Check for legal links (these should still be present)
    expect(screen.getByRole('link', { name: 'Cookies Policy' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Privacy Policy' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Terms of Service' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Help Center' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Changelog' })).toBeInTheDocument();

    // Check for copyright
    expect(screen.getByText(/© \d{4} Trip Radar\. All rights reserved\./)).toBeInTheDocument();
  });

  it('should have proper simplified layout structure', () => {
    const { container } = renderFooter();
    const footer = container.querySelector('footer');

    // Footer should be visible by default (not on auth page)
    expect(footer).not.toHaveClass('hidden');
    expect(footer).toHaveClass('bg-surface', 'dark:bg-surface-dark');
  });

  it('should apply custom className when provided', () => {
    const { container } = renderFooter({ className: 'custom-footer' });
    const footer = container.querySelector('footer');

    expect(footer).toHaveClass('custom-footer');
  });

  it('should have centered layout with flex column', () => {
    const { container } = renderFooter();

    // Check for centered flex layout instead of grid
    const flexContainer = container.querySelector('.flex.flex-col.items-center');
    expect(flexContainer).toBeInTheDocument();
  });
});
