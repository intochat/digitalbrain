import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { CookiePolicy } from './CookiePolicy';

const renderCookiePolicy = () => {
  return render(
    <BrowserRouter>
      <CookiePolicy />
    </BrowserRouter>
  );
};

describe('CookiePolicy', () => {
  it('renders page header and update date', () => {
    renderCookiePolicy();

    expect(screen.getByRole('heading', { name: 'Cookie Policy', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('Last updated: February 24, 2026')).toBeInTheDocument();
  });

  it('renders all cookie categories', () => {
    renderCookiePolicy();

    expect(screen.getByRole('heading', { name: 'Strictly Necessary Cookies' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Functional Cookies' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Analytics Cookies' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Advertising Cookies' })).toBeInTheDocument();
  });

  it('contains links to related legal pages and contact email', () => {
    renderCookiePolicy();

    expect(screen.getByRole('link', { name: 'Cookie Preferences' })).toHaveAttribute(
      'href',
      '/cookies#cookie-preferences'
    );
    expect(screen.getByRole('link', { name: 'Privacy Policy' })).toHaveAttribute('href', '/privacy');
    expect(screen.getByRole('link', { name: 'Terms of Service' })).toHaveAttribute('href', '/terms');
    expect(screen.getByRole('link', { name: 'privacy@tripradar.io' })).toHaveAttribute(
      'href',
      'mailto:privacy@tripradar.io'
    );
  });
});
