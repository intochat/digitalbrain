import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { FieldSection } from './FieldSection';

describe('FieldSection', () => {
  it('should render without title and description', () => {
    render(
      <FieldSection>
        <div>Test content</div>
      </FieldSection>
    );

    expect(screen.getByText('Test content')).toBeInTheDocument();
  });

  it('should render with title and description', () => {
    render(
      <FieldSection title="Test Section" description="Test description">
        <div>Test content</div>
      </FieldSection>
    );

    expect(screen.getByText('Test Section')).toBeInTheDocument();
    expect(screen.getByText('Test description')).toBeInTheDocument();
    expect(screen.getByText('Test content')).toBeInTheDocument();
  });

  it('should apply compact variant styling', () => {
    const { container } = render(
      <FieldSection variant="compact" title="Compact Section">
        <div>Test content</div>
      </FieldSection>
    );

    const sectionElement = container.firstChild as HTMLElement;
    expect(sectionElement).toHaveClass('space-y-3');
  });

  it('should apply default variant styling', () => {
    const { container } = render(
      <FieldSection title="Default Section">
        <div>Test content</div>
      </FieldSection>
    );

    const sectionElement = container.firstChild as HTMLElement;
    expect(sectionElement).toHaveClass('space-y-4');
  });
});
