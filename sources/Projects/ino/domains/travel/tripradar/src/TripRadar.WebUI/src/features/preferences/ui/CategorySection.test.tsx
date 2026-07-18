import React from 'react';
import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { CategorySection } from './CategorySection';

describe('CategorySection', () => {
  it('renders title and description', () => {
    render(
      <CategorySection title="Test Category" description="Test description">
        <div>Test Content</div>
      </CategorySection>
    );

    expect(screen.getByText('Test Category')).toBeInTheDocument();
    expect(screen.getByText('Test description')).toBeInTheDocument();
  });

  it('renders children content', () => {
    render(
      <CategorySection title="Test Category" description="Test description">
        <div>Child Content 1</div>
        <div>Child Content 2</div>
      </CategorySection>
    );

    expect(screen.getByText('Child Content 1')).toBeInTheDocument();
    expect(screen.getByText('Child Content 2')).toBeInTheDocument();
  });

  it('applies custom className', () => {
    const { container } = render(
      <CategorySection title="Test Category" description="Test description" className="custom-class">
        <div>Test Content</div>
      </CategorySection>
    );

    expect(container.firstChild).toHaveClass('custom-class');
  });
});
