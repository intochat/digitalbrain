import { render, screen, fireEvent } from '@testing-library/react';
import { NumberField } from './NumberField';

describe('NumberField', () => {
  it('renders with label and value', () => {
    const mockOnChange = vi.fn();

    render(<NumberField label="Test Number" value={5} onChange={mockOnChange} />);

    expect(screen.getByText('Test Number')).toBeInTheDocument();
    expect(screen.getByDisplayValue('5')).toBeInTheDocument();
  });

  it('calls onChange when value changes', () => {
    const mockOnChange = vi.fn();

    render(<NumberField label="Test Number" value={5} onChange={mockOnChange} />);

    const input = screen.getByDisplayValue('5');
    fireEvent.change(input, { target: { value: '10' } });

    expect(mockOnChange).toHaveBeenCalledWith(10);
  });

  it('enforces min/max constraints', () => {
    const mockOnChange = vi.fn();

    render(<NumberField label="Test Number" value={5} min={1} max={10} onChange={mockOnChange} />);

    const input = screen.getByRole('spinbutton', { name: 'Test Number' });
    expect(input).toHaveAttribute('min', '1');
    expect(input).toHaveAttribute('max', '10');
  });

  it('shows error message when error prop is provided', () => {
    const mockOnChange = vi.fn();

    render(<NumberField label="Test Number" value={5} onChange={mockOnChange} error="Invalid number" />);

    expect(screen.getByText('Invalid number')).toBeInTheDocument();
  });
});
