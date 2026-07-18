import { Search, ExternalLink } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import { getArray, getString, isTruncatedWrapperPayload, safeParse } from './parseHistoryData';

interface GoogleSearchCardProps {
  item: TripHistoryItem;
}

interface ParsedGoogleSearchItem {
  title: string;
  link: string;
  snippet: string | null;
  displayedLink: string | null;
  position: number | null;
}

const parseGoogleSearchData = (data: Record<string, unknown>): ParsedGoogleSearchItem[] => {
  const organicResults = getArray(data, 'organicResults') ?? getArray(data, 'organic_results') ?? [];
  const videoResults = getArray(data, 'inline_videos') ?? getArray(data, 'video_results') ?? [];

  const allResults = [...organicResults, ...videoResults];

  if (allResults.length > 0) {
    return allResults.slice(0, 5).map(item => {
      const p = item as Record<string, unknown>;
      return {
        title: getString(p, 'title') ?? 'Unknown Result',
        link: getString(p, 'link') ?? '#',
        snippet: getString(p, 'snippet') ?? getString(p, 'description'),
        displayedLink: getString(p, 'displayed_link') ?? getString(p, 'visible_url') ?? getString(p, 'source'),
        position: getNumber(p, 'position'),
      };
    });
  }

  return [];
};

export const GoogleSearchCard = ({ item }: GoogleSearchCardProps) => {
  const data = safeParse(item.resultSummary);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  const results = parseGoogleSearchData(data);

  if (results.length === 0) {
    return (
      <p className="pt-0.5 text-xs text-content-secondary dark:text-content-secondary-dark italic">
        No Google search results found.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      {results.map((result, index) => (
        <div key={`search-result-${index}`} className="flex flex-col gap-1">
          <div className="flex items-start gap-2">
            <a
              href={result.link}
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary-600 dark:text-primary-400 hover:text-primary-800 dark:hover:text-primary-300 font-medium text-sm leading-tight group flex items-center gap-1.5"
            >
              <Search className="h-3.5 w-3.5 mt-0.5 text-content-secondary group-hover:text-primary-600 dark:group-hover:text-primary-300 transition-colors" />
              {result.title}
              <ExternalLink className="h-3 w-3 opacity-0 group-hover:opacity-100 transition-opacity" />
            </a>
          </div>

          {result.displayedLink && (
            <p className="text-[10px] text-emerald-700 dark:text-emerald-400 truncate pl-5">{result.displayedLink}</p>
          )}

          {result.snippet && (
            <p className="text-xs text-content-secondary dark:text-content-secondary-dark leading-relaxed pl-5 line-clamp-2">
              {result.snippet}
            </p>
          )}
        </div>
      ))}
    </div>
  );
};

const getNumber = (obj: Record<string, unknown>, key: string): number | null => {
  const val = obj[key];
  if (typeof val === 'number') return val;
  if (typeof val === 'string' && !isNaN(parseFloat(val))) return parseFloat(val);
  return null;
};
