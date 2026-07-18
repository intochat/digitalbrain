import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { NameInlineEditor } from './NameInlineEditor';

describe('NameInlineEditor', () => {
  const defaultProps = {
    firstName: 'John',
    lastName: 'Doe',
    onSave: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders display mode with full name', () => {
    render(<NameInlineEditor {...defaultProps} />);

    expect(screen.getByText('Full Name')).toBeInTheDocument();
    expect(screen.getByText('John Doe')).toBeInTheDocument();
  });

  it('renders "Not set" when no names provided', () => {
    render(<NameInlineEditor {...defaultProps} firstName="" lastName="" />);

    expect(screen.getByText('Not set')).toBeInTheDocument();
  });

  it('renders only first name when last name is empty', () => {
    render(<NameInlineEditor {...defaultProps} lastName="" />);

    expect(screen.getByText('John')).toBeInTheDocument();
  });

  it('enters edit mode when edit button is clicked', async () => {
    render(<NameInlineEditor {...defaultProps} />);

    const editButton = screen.getByTitle('Edit name');
    fireEvent.click(editButton);

    await waitFor(() => {
      const inputs = screen.getAllByRole('textbox');
      expect(inputs).toHaveLength(2);
      expect(screen.getByDisplayValue('John')).toBeInTheDocument();
      expect(screen.getByDisplayValue('Doe')).toBeInTheDocument();
    });
  });

  it('calls onSave with both names when save button is clicked', async () => {
    const mockOnSave = vi.fn().mockResolvedValue(undefined);
    render(<NameInlineEditor {...defaultProps} onSave={mockOnSave} />);

    // Enter edit mode
    const editButton = screen.getByTitle('Edit name');
    fireEvent.click(editButton);

    // Change values
    const inputs = screen.getAllByRole('textbox');
    fireEvent.change(inputs[0], { target: { value: 'Jane' } });
    fireEvent.change(inputs[1], { target: { value: 'Smith' } });

    // Save
    const saveButton = screen.getByText('Save');
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(mockOnSave).toHaveBeenCalledWith('Jane', 'Smith');
    });
  });

  it('cancels edit mode when cancel button is clicked', async () => {
    render(<NameInlineEditor {...defaultProps} />);

    // Enter edit mode
    const editButton = screen.getByTitle('Edit name');
    fireEvent.click(editButton);

    // Change values
    const inputs = screen.getAllByRole('textbox');
    fireEvent.change(inputs[0], { target: { value: 'Jane' } });

    // Cancel
    const cancelButton = screen.getByText('Cancel');
    fireEvent.click(cancelButton);

    await waitFor(() => {
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
      expect(screen.getByText('John Doe')).toBeInTheDocument();
    });
  });

  it('shows loading state when isLoading is true', () => {
    render(<NameInlineEditor {...defaultProps} isLoading={true} />);

    expect(screen.getByTitle('Edit name')).toBeDisabled();
  });
});
