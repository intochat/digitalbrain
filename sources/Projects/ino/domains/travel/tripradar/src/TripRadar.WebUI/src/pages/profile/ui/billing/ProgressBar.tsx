interface ProgressBarProps {
  percentage: number;
  className?: string;
}

export const ProgressBar = ({ percentage, className = '' }: ProgressBarProps) => {
  const clampedPercentage = Math.min(Math.max(percentage, 0), 100);

  return (
    <div className={`w-full bg-surface-accent dark:bg-surface-accent-dark rounded-full h-2 ${className}`}>
      <div
        className="bg-content dark:bg-content-dark h-2 rounded-full transition-all duration-300"
        style={{ width: `${clampedPercentage}%` }}
      />
    </div>
  );
};
