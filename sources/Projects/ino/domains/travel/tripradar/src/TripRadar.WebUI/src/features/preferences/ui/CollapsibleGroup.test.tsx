import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { CollapsibleGroup } from './CollapsibleGroup';

describe('CollapsibleGroup', () => {
  it('renders with title and children when expanded', () => {
    render(
      <CollapsibleGroup title="Test Group" isExpanded={true} onToggle={vi.fn()}>
        <div>Test Content</div>
      </CollapsibleGroup>
    );

    expect(screen.getByText('Test Group')).toBeInTheDocument();
    expect(screen.getByText('Test Content')).toBeInTheDocument();
  });

  it('hides children when collapsed', () => {
    render(
      <CollapsibleGroup title="Test Group" isExpanded={false} onToggle={vi.fn()}>
        <div>Test Content</div>
      </CollapsibleGroup>
    );

    expect(screen.getByText('Test Group')).toBeInTheDocument();

    // Content should be in DOM but hidden (aria-hidden="true")
    const content = screen.getByText('Test Content');
    expect(content.closest('[aria-hidden="true"]')).toBeInTheDocument();
  });

  it('calls onToggle when header is clicked', () => {
    const onToggle = vi.fn();

    render(
      <CollapsibleGroup title="Test Group" isExpanded={false} onToggle={onToggle}>
        <div>Test Content</div>
      </CollapsibleGroup>
    );

    fireEvent.click(screen.getByText('Test Group'));
    expect(onToggle).toHaveBeenCalledTimes(1);
  });

  it('shows correct arrow direction based on expanded state', () => {
    const { rerender } = render(
      <CollapsibleGroup title="Test Group" isExpanded={false} onToggle={vi.fn()}>
        <div>Test Content</div>
      </CollapsibleGroup>
    );

    // Check collapsed state (right arrow)
    const button = screen.getByRole('button', { expanded: false });
    expect(button).toBeInTheDocument();

    // Check expanded state (down arrow)
    rerender(
      <CollapsibleGroup title="Test Group" isExpanded={true} onToggle={vi.fn()}>
        <div>Test Content</div>
      </CollapsibleGroup>
    );

    const expandedButton = screen.getByRole('button', { expanded: true });
    expect(expandedButton).toBeInTheDocument();
  });
});
