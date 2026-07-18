import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { GroupHeader } from './GroupHeader';

describe('GroupHeader', () => {
  it('renders title correctly', () => {
    render(<GroupHeader title="Test Header" isExpanded={false} onClick={vi.fn()} />);

    expect(screen.getByText('Test Header')).toBeInTheDocument();
  });

  it('calls onClick when clicked', () => {
    const onClick = vi.fn();

    render(<GroupHeader title="Test Header" isExpanded={false} onClick={onClick} />);

    fireEvent.click(screen.getByRole('button'));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('sets correct aria-expanded attribute', () => {
    const { rerender } = render(<GroupHeader title="Test Header" isExpanded={false} onClick={vi.fn()} />);

    expect(screen.getByRole('button')).toHaveAttribute('aria-expanded', 'false');

    rerender(<GroupHeader title="Test Header" isExpanded={true} onClick={vi.fn()} />);

    expect(screen.getByRole('button')).toHaveAttribute('aria-expanded', 'true');
  });

  it('has proper accessibility attributes', () => {
    render(
      <GroupHeader
        title="Test Header"
        isExpanded={false}
        onClick={vi.fn()}
        aria-controls="test-content-id"
        id="test-header-id"
      />
    );

    const button = screen.getByRole('button');
    expect(button).toHaveAttribute('aria-controls', 'test-content-id');
    expect(button).toHaveAttribute('type', 'button');
    expect(button).toHaveAttribute('id', 'test-header-id');
    expect(button).toHaveAttribute('aria-describedby', 'test-header-id-description');
  });

  it('has minimum touch target size for mobile', () => {
    render(<GroupHeader title="Test Header" isExpanded={false} onClick={vi.fn()} />);

    const button = screen.getByRole('button');
    expect(button).toHaveClass('min-h-[48px]');
    expect(button).toHaveClass('touch-manipulation');
  });
});
