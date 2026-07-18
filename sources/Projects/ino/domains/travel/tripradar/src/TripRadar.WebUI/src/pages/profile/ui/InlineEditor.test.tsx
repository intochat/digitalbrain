import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { InlineEditor } from './InlineEditor';

describe('InlineEditor', () => {
  const defaultProps = {
    value: 'Test Value',
    onSave: vi.fn(),
    label: 'Test Field',
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders display mode by default', () => {
    render(<InlineEditor {...defaultProps} />);

    expect(screen.getByText('Test Field')).toBeInTheDocument();
    expect(screen.getByText('Test Value')).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('enters edit mode when edit button is clicked', async () => {
    render(<InlineEditor {...defaultProps} />);

    const editButton = screen.getByRole('button', { name: 'Edit Test Field' });
    fireEvent.click(editButton);

    await waitFor(() => {
      expect(screen.getByRole('textbox')).toBeInTheDocument();
      expect(screen.getByDisplayValue('Test Value')).toBeInTheDocument();
    });
  });

  it('calls onSave when save button is clicked', async () => {
    const mockOnSave = vi.fn().mockResolvedValue(undefined);
    render(<InlineEditor {...defaultProps} onSave={mockOnSave} />);

    // Enter edit mode
    const editButton = screen.getByRole('button', { name: 'Edit Test Field' });
    fireEvent.click(editButton);

    // Change value
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'New Value' } });

    // Save
    const saveButton = screen.getByTitle('Save changes');
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(mockOnSave).toHaveBeenCalledWith('New Value');
    });
  });

  it('cancels edit mode when cancel button is clicked', async () => {
    render(<InlineEditor {...defaultProps} />);

    // Enter edit mode
    const editButton = screen.getByRole('button', { name: 'Edit Test Field' });
    fireEvent.click(editButton);

    // Change value
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'New Value' } });

    // Cancel
    const cancelButton = screen.getByTitle('Cancel changes');
    fireEvent.click(cancelButton);

    await waitFor(() => {
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
      expect(screen.getByText('Test Value')).toBeInTheDocument();
    });
  });

  it('shows loading state when isLoading is true', () => {
    render(<InlineEditor {...defaultProps} isLoading={true} />);

    expect(screen.getByRole('button', { name: 'Edit Test Field' })).toBeDisabled();
  });

  it('shows placeholder when value is empty', () => {
    render(<InlineEditor {...defaultProps} value="" placeholder="Enter value..." />);

    expect(screen.getByText('Enter value...')).toBeInTheDocument();
  });
});
