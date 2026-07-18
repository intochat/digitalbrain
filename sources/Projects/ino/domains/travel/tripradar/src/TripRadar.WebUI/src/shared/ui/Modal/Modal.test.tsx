import { render, screen, fireEvent } from '@testing-library/react';
import { Modal } from './Modal';

describe('Modal', () => {
  const defaultProps = {
    isOpen: true,
    onClose: vi.fn(),
    children: <div>Modal content</div>,
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders when open', () => {
    render(<Modal {...defaultProps} />);

    expect(screen.getByText('Modal content')).toBeInTheDocument();
  });

  it('does not render when closed', () => {
    render(<Modal {...defaultProps} isOpen={false} />);

    expect(screen.queryByText('Modal content')).not.toBeInTheDocument();
  });

  it('renders title when provided', () => {
    render(<Modal {...defaultProps} title="Test Modal" />);

    expect(screen.getByText('Test Modal')).toBeInTheDocument();
  });

  it('calls onClose when close button is clicked', () => {
    const onClose = vi.fn();
    render(<Modal {...defaultProps} onClose={onClose} title="Test Modal" />);

    const closeButton = screen.getByLabelText('Close modal');
    fireEvent.click(closeButton);

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when backdrop is clicked', () => {
    const onClose = vi.fn();
    render(<Modal {...defaultProps} onClose={onClose} />);

    // Find the backdrop div (first child of the portal)
    const backdrop = document.querySelector('.fixed.inset-0.bg-black\\/60');
    fireEvent.click(backdrop!);

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does not call onClose when backdrop click is disabled', () => {
    const onClose = vi.fn();
    render(<Modal {...defaultProps} onClose={onClose} closeOnBackdropClick={false} />);

    // Find the backdrop div (first child of the portal)
    const backdrop = document.querySelector('.fixed.inset-0.bg-black\\/60');
    fireEvent.click(backdrop!);

    expect(onClose).not.toHaveBeenCalled();
  });
});
