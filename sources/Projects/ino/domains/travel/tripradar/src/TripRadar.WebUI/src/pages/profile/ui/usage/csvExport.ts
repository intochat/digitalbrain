import { SOURCE_ORDER, SOURCE_META } from './usageConstants';
import type { DayTimelinePoint } from './usageUtils';

const BOM = '\uFEFF';

const escapeCsvField = (value: string): string => {
  if (value.includes(',') || value.includes('"') || value.includes('\n')) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
};

export const buildUsageCsv = (timeline: DayTimelinePoint[]): string => {
  const sourceHeaders = SOURCE_ORDER.map(s => SOURCE_META[s].labelKey);
  const header = ['Date', 'Total Tokens', 'Events', ...sourceHeaders].map(escapeCsvField).join(',');

  const rows = timeline.map(point => {
    const sourceValues = SOURCE_ORDER.map(source => {
      const entry = point.breakdown.find(b => b.source === source);
      return String(entry?.tokens ?? 0);
    });
    return [point.date, String(point.totalTokens), String(point.eventsCount), ...sourceValues].join(',');
  });

  return BOM + [header, ...rows].join('\n');
};

export const downloadCsv = (timeline: DayTimelinePoint[], fromDate: string, toDate: string): void => {
  const csv = buildUsageCsv(timeline);
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `usage_${fromDate}_${toDate}.csv`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
};
