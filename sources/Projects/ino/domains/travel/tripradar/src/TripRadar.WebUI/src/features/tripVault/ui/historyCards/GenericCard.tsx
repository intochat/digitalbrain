import { useState } from 'react';
import { ChevronDown, ChevronRight, FileJson } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { extractHighlights, safeParse } from './parseHistoryData';

interface GenericCardProps {
  item: TripHistoryItem;
}

export const GenericCard = ({ item }: GenericCardProps) => {
  const [expanded, setExpanded] = useState(false);
  const data = safeParse(item.resultSummary);
  const highlights = data ? extractHighlights(data, 6) : [];

  return (
    <div className="space-y-2">
      {/* Key-value highlights */}
      {highlights.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {highlights.map(highlight => (
            <span
              key={highlight.key}
              className="inline-flex items-center rounded-lg bg-surface-accent/50 dark:bg-surface-accent-dark/30 px-2.5 py-1 text-[11px] text-content-secondary dark:text-content-secondary-dark"
            >
              <span className="font-medium text-content dark:text-content-dark mr-1.5">{highlight.key}:</span>
              {highlight.value}
            </span>
          ))}
        </div>
      )}

      {/* Collapsible raw JSON */}
      {item.resultSummary && (
        <button
          type="button"
          onClick={() => setExpanded(!expanded)}
          className="inline-flex items-center gap-1.5 text-[11px] font-medium text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark transition-colors"
        >
          {expanded ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
          <FileJson className="h-3.5 w-3.5" />
          {expanded ? 'Hide raw data' : 'Show raw data'}
        </button>
      )}

      {expanded && item.resultSummary && (
        <pre className="max-h-48 overflow-auto rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-slate-50 dark:bg-slate-900/50 px-3 py-2 text-[11px] text-content-secondary dark:text-content-secondary-dark font-mono leading-relaxed">
          {formatJson(item.resultSummary)}
        </pre>
      )}

      {!item.resultSummary && (
        <p className="text-xs text-content-secondary dark:text-content-secondary-dark">No response data available.</p>
      )}
    </div>
  );
};

const formatJson = (json: string): string => {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
};
