import { render, screen, fireEvent } from '@testing-library/react';
import { SelectField } from './SelectField';

const mockOptions = [
  { value: 'option1', label: 'Option 1' },
  { value: 'option2', label: 'Option 2' },
  { value: 'option3', label: 'Option 3' },
];

describe('SelectField', () => {
  it('renders with label and options', () => {
    const mockOnChange = vi.fn();

    render(<SelectField label="Test Select" value="option1" options={mockOptions} onChange={mockOnChange} />);

    expect(screen.getByText('Test Select')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Test Select' })).toHaveTextContent('Option 1');
  });

  it('calls onChange when selection changes', () => {
    const mockOnChange = vi.fn();

    render(<SelectField label="Test Select" value="option1" options={mockOptions} onChange={mockOnChange} />);

    fireEvent.click(screen.getByRole('button', { name: 'Test Select' }));
    fireEvent.click(screen.getByRole('option', { name: 'Option 2' }));

    expect(mockOnChange).toHaveBeenCalledWith('option2');
  });

  it('shows error message when error prop is provided', () => {
    const mockOnChange = vi.fn();

    render(
      <SelectField
        label="Test Select"
        value="option1"
        options={mockOptions}
        onChange={mockOnChange}
        error="This field is required"
      />
    );

    expect(screen.getByText('This field is required')).toBeInTheDocument();
  });

  it('shows required indicator when required prop is true', () => {
    const mockOnChange = vi.fn();

    render(<SelectField label="Test Select" value="option1" options={mockOptions} onChange={mockOnChange} required />);

    expect(screen.getByText('*')).toBeInTheDocument();
  });
});
