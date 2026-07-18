import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { HelpCenter } from './HelpCenter';

const renderHelpCenter = () => {
  return render(
    <BrowserRouter>
      <HelpCenter />
    </BrowserRouter>
  );
};

describe('HelpCenter', () => {
  it('renders main heading and key sections', () => {
    renderHelpCenter();

    expect(screen.getByRole('heading', { name: 'Popular topics', level: 2 })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Frequently asked questions', level: 2 })).toBeInTheDocument();
  });

  it('renders support actions with expected destinations', () => {
    renderHelpCenter();

    expect(screen.getByRole('link', { name: 'View pricing' })).toHaveAttribute('href', '/pricing');
    expect(screen.getByRole('link', { name: 'Open feedback page' })).toHaveAttribute('href', '/feedback');
    expect(screen.getByRole('link', { name: 'support@tripradar.io' })).toHaveAttribute(
      'href',
      'mailto:support@tripradar.io'
    );
  });
});
