import { render, screen, fireEvent } from '@testing-library/react';
import { ToggleField } from './ToggleField';

describe('ToggleField', () => {
  it('renders with label', () => {
    const mockOnChange = vi.fn();

    render(<ToggleField label="Test Toggle" value={false} onChange={mockOnChange} />);

    expect(screen.getByText('Test Toggle')).toBeInTheDocument();
    expect(screen.getByRole('switch')).toBeInTheDocument();
  });

  it('calls onChange when clicked', () => {
    const mockOnChange = vi.fn();

    render(<ToggleField label="Test Toggle" value={false} onChange={mockOnChange} />);

    const toggle = screen.getByRole('switch');
    fireEvent.click(toggle);

    expect(mockOnChange).toHaveBeenCalledWith(true);
  });

  it('shows description when provided', () => {
    const mockOnChange = vi.fn();

    render(
      <ToggleField label="Test Toggle" description="This is a test toggle" value={false} onChange={mockOnChange} />
    );

    expect(screen.getByText('This is a test toggle')).toBeInTheDocument();
  });

  it('handles keyboard navigation', () => {
    const mockOnChange = vi.fn();

    render(<ToggleField label="Test Toggle" value={false} onChange={mockOnChange} />);

    const toggle = screen.getByRole('switch');
    fireEvent.keyDown(toggle, { key: ' ' });

    expect(mockOnChange).toHaveBeenCalledWith(true);
  });

  it('has mobile-friendly responsive classes', () => {
    const mockOnChange = vi.fn();

    render(<ToggleField label="Test Toggle" value={false} onChange={mockOnChange} />);

    const toggle = screen.getByRole('switch');
    expect(toggle).toHaveClass('touch-manipulation');
    expect(toggle).toHaveClass('flex-shrink-0');

    const label = screen.getByText('Test Toggle');
    expect(label.closest('div')).toHaveClass('min-w-0');
  });
});
