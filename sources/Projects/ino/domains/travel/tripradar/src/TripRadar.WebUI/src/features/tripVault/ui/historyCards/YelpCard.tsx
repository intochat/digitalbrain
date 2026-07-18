import { Star } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import {
  formatRating,
  getArray,
  getNumber,
  getObject,
  getString,
  isTruncatedWrapperPayload,
  safeParse,
  truncateText,
} from './parseHistoryData';
import { ResultImage } from './ResultImage';

interface YelpCardProps {
  item: TripHistoryItem;
}

interface ParsedYelpBusiness {
  name: string;
  rating: number | null;
  reviewCount: number | null;
  category: string | null;
  priceRange: string | null;
  address: string | null;
  thumbnail: string | null;
  phone: string | null;
}

interface ParsedYelpMenuItem {
  name: string;
  price: string | null;
  description: string | null;
}

interface ParsedYelpMenuSection {
  name: string;
  items: ParsedYelpMenuItem[];
}

const matchesServiceType = (actual: unknown, ...expected: string[]): boolean => {
  const normalized = String(actual ?? '').toLowerCase();
  return expected.some(type => type.toLowerCase() === normalized);
};

const parseBusinesses = (data: Record<string, unknown>): ParsedYelpBusiness[] => {
  const organicResults =
    getArray(data, 'organicResults') ?? getArray(data, 'organic_results') ?? getArray(data, 'results') ?? [];

  return organicResults.slice(0, 5).map(biz => {
    const b = biz as Record<string, unknown>;
    const categories = getArray(b, 'categories');

    return {
      name: getString(b, 'name') ?? getString(b, 'title') ?? 'Unknown',
      rating: getNumber(b, 'rating'),
      reviewCount: getNumber(b, 'reviewCount') ?? getNumber(b, 'review_count') ?? getNumber(b, 'reviews'),
      category:
        categories && categories.length > 0
          ? typeof categories[0] === 'string'
            ? categories[0]
            : (getString(categories[0] as Record<string, unknown>, 'title') ?? null)
          : (getString(b, 'category') ?? getString(b, 'type') ?? null),
      priceRange: getString(b, 'priceRange') ?? getString(b, 'price_range') ?? getString(b, 'price'),
      address: getString(b, 'address') ?? getString(b, 'neighborhoods'),
      thumbnail: getString(b, 'thumbnail'),
      phone: getString(b, 'phone'),
    };
  });
};

/** Parse single-place Yelp result */
const parseSinglePlace = (data: Record<string, unknown>): ParsedYelpBusiness | null => {
  const place = getObject(data, 'place_results') ?? getObject(data, 'placeResults') ?? data;

  const name = getString(place, 'name') ?? getString(place, 'title');
  if (!name) {
    return null;
  }

  const categories = getArray(place, 'categories');
  const firstCategory =
    categories && categories.length > 0
      ? typeof categories[0] === 'string'
        ? categories[0]
        : getString(categories[0] as Record<string, unknown>, 'title')
      : null;
  const images = getArray(place, 'images');
  const firstImage = images && images.length > 0 && typeof images[0] === 'string' ? images[0] : null;

  return {
    name,
    rating: getNumber(place, 'rating'),
    reviewCount: getNumber(place, 'reviewCount') ?? getNumber(place, 'review_count') ?? getNumber(place, 'reviews'),
    category: firstCategory ?? getString(place, 'category') ?? getString(place, 'type'),
    priceRange: getString(place, 'priceRange') ?? getString(place, 'price_range') ?? getString(place, 'price'),
    address: getString(place, 'address') ?? getString(place, 'neighborhoods'),
    thumbnail: getString(place, 'thumbnail') ?? firstImage,
    phone: getString(place, 'phone'),
  };
};

/** Parse user reviews from a YelpReviews response */
interface ParsedYelpReview {
  userName: string;
  rating: number | null;
  content: string;
  date: string | null;
}

const parseYelpReviews = (data: Record<string, unknown>): ParsedYelpReview[] => {
  const reviews = getArray(data, 'reviews') ?? [];
  return reviews.slice(0, 3).map(r => {
    const review = r as Record<string, unknown>;
    const user = getObject(review, 'user');
    return {
      userName: user ? (getString(user, 'name') ?? 'User') : 'User',
      rating: getNumber(review, 'rating'),
      content: getString(review, 'comment', 'text') ?? getString(review, 'text') ?? getString(review, 'snippet') ?? '',
      date: getString(review, 'date') ?? getString(review, 'time_created'),
    };
  });
};

const parseMenuSections = (data: Record<string, unknown>): ParsedYelpMenuSection[] => {
  const fullMenu = getObject(data, 'full_menu_results') ?? getObject(data, 'fullMenuResults');
  if (!fullMenu) {
    return [];
  }

  const sections = getArray(fullMenu, 'sections') ?? [];
  return sections.slice(0, 3).map(sectionRaw => {
    const section = sectionRaw as Record<string, unknown>;
    const items = getArray(section, 'items') ?? [];

    return {
      name: getString(section, 'name') ?? getString(section, 'title') ?? 'Menu section',
      items: items.slice(0, 4).map(itemRaw => {
        const item = itemRaw as Record<string, unknown>;
        return {
          name: getString(item, 'name') ?? getString(item, 'title') ?? 'Menu item',
          price: getString(item, 'price'),
          description: getString(item, 'description'),
        };
      }),
    };
  });
};

const normalizeFullMenuState = (
  fullMenuState: string,
  menuName: string | null,
  businessName: string | null
): string => {
  const normalizedState = fullMenuState.toLowerCase();
  const isInvalidRequestedMenu =
    normalizedState.includes('is not valid') &&
    normalizedState.includes('showing place results instead of full menu results');

  if (!isInvalidRequestedMenu) {
    return fullMenuState;
  }

  const requestedMenuName = menuName?.trim();
  const placeName = businessName?.trim();

  if (requestedMenuName && placeName) {
    return `Menu "${requestedMenuName}" is not available for ${placeName}. Showing place results instead.`;
  }

  if (requestedMenuName) {
    return `Menu "${requestedMenuName}" is not available. Showing place results instead.`;
  }

  if (placeName) {
    return `The requested menu is not available for ${placeName}. Showing place results instead.`;
  }

  return 'The requested menu is not available. Showing place results instead.';
};

export const YelpCard = ({ item }: YelpCardProps) => {
  const data = safeParse(item.resultSummary);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  // Check if it's reviews data or search/place data
  const isReviews = matchesServiceType(item.serviceType, 'YelpReviews') || getArray(data, 'reviews') != null;
  const isPlace =
    matchesServiceType(item.serviceType, 'YelpPlace', 'YelpPlaceFullMenu') ||
    getObject(data, 'place_results') != null ||
    getObject(data, 'placeResults') != null;
  const businesses = isPlace || isReviews ? [] : parseBusinesses(data);
  const singlePlace = isPlace ? parseSinglePlace(data) : null;
  const reviews = isReviews ? parseYelpReviews(data) : [];
  const menuSections = parseMenuSections(data);
  const isFullMenuRequest = matchesServiceType(item.serviceType, 'YelpPlaceFullMenu');

  const placeRoot = getObject(data, 'place_results') ?? getObject(data, 'placeResults') ?? data;
  const businessName = getString(placeRoot, 'name') ?? getString(placeRoot, 'title');
  const businessRating = getNumber(placeRoot, 'rating');
  const searchInformation = getObject(data, 'search_information') ?? getObject(data, 'searchInformation');
  const fullMenuState = searchInformation
    ? (getString(searchInformation, 'full_menu_results_state') ?? getString(searchInformation, 'fullMenuResultsState'))
    : null;
  const searchParameters = getObject(data, 'search_parameters') ?? getObject(data, 'searchParameters');
  const menuName = searchParameters
    ? (getString(searchParameters, 'menu_name') ?? getString(searchParameters, 'menuName'))
    : null;
  const fullMenuStateMessage = fullMenuState
    ? normalizeFullMenuState(fullMenuState, menuName, singlePlace?.name ?? businessName ?? null)
    : null;

  return (
    <div className="space-y-3">
      {/* Single place header */}
      {(singlePlace ?? (isReviews && businessName)) && (
        <div className="flex items-center gap-3 flex-wrap">
          <div className="inline-flex items-center gap-2 rounded-lg bg-red-50 dark:bg-red-500/10 px-3 py-1.5 text-sm font-semibold text-red-700 dark:text-red-300">
            <Star className="h-4 w-4" />
            {singlePlace?.name ?? businessName}
          </div>
          {(singlePlace?.rating ?? businessRating) != null && (
            <span className="text-xs text-amber-600 dark:text-amber-400">
              {formatRating(singlePlace?.rating ?? businessRating)}
            </span>
          )}
          {singlePlace?.reviewCount != null && (
            <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
              ({singlePlace.reviewCount.toLocaleString()} reviews)
            </span>
          )}
          {singlePlace?.category && (
            <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
              · {singlePlace.category}
            </span>
          )}
          {singlePlace?.priceRange && (
            <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
              · {singlePlace.priceRange}
            </span>
          )}
        </div>
      )}

      {/* Yelp full menu status */}
      {isFullMenuRequest && fullMenuStateMessage && (
        <div className="rounded-lg border border-amber-200/70 bg-amber-50/80 px-3 py-2 text-[11px] leading-relaxed text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300">
          {truncateText(fullMenuStateMessage, 260)}
        </div>
      )}

      {/* Yelp full menu sections */}
      {menuSections.length > 0 && (
        <div className="space-y-2">
          {menuSections.map((section, sectionIndex) => (
            <div
              key={`yelp-menu-section-${sectionIndex}`}
              className="rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <p className="text-xs font-semibold text-content dark:text-content-dark">{section.name}</p>
              <div className="mt-1.5 space-y-1.5">
                {section.items.map((menuItem, itemIndex) => (
                  <div key={`yelp-menu-item-${sectionIndex}-${itemIndex}`} className="text-[11px]">
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-medium text-content dark:text-content-dark">{menuItem.name}</span>
                      {menuItem.price && (
                        <span className="text-content-secondary dark:text-content-secondary-dark">
                          {menuItem.price}
                        </span>
                      )}
                    </div>
                    {menuItem.description && (
                      <p className="text-content-secondary dark:text-content-secondary-dark">
                        {truncateText(menuItem.description, 140)}
                      </p>
                    )}
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Business list (search results) */}
      {businesses.length > 0 && (
        <div className="space-y-2">
          {businesses.map((biz, index) => (
            <div
              key={`yelp-biz-${index}`}
              className="flex items-center gap-3 rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <ResultImage src={biz.thumbnail} alt={biz.name} variant="restaurant" />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-medium text-content dark:text-content-dark truncate">{biz.name}</p>
                <div className="flex items-center gap-2 text-[11px] text-content-secondary dark:text-content-secondary-dark">
                  {biz.rating != null && <span>{formatRating(biz.rating)}</span>}
                  {biz.reviewCount != null && <span>({biz.reviewCount})</span>}
                  {biz.category && <span>· {biz.category}</span>}
                  {biz.priceRange && <span>· {biz.priceRange}</span>}
                </div>
                {biz.address && (
                  <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark truncate mt-0.5">
                    {truncateText(biz.address, 50)}
                  </p>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Yelp Reviews */}
      {reviews.length > 0 && (
        <div className="space-y-2">
          {reviews.map((review, index) => (
            <div
              key={`yelp-review-${index}`}
              className="rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <div className="flex items-center gap-2 text-[11px]">
                <span className="font-medium text-content dark:text-content-dark">{review.userName}</span>
                {review.rating != null && (
                  <span className="text-amber-600 dark:text-amber-400">{formatRating(review.rating)}</span>
                )}
                {review.date && (
                  <span className="text-content-secondary dark:text-content-secondary-dark">· {review.date}</span>
                )}
              </div>
              {review.content && (
                <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark leading-relaxed mt-0.5">
                  "{truncateText(review.content, 200)}"
                </p>
              )}
            </div>
          ))}
        </div>
      )}

      {businesses.length === 0 &&
        reviews.length === 0 &&
        menuSections.length === 0 &&
        !singlePlace &&
        !businessName && (
          <p className="pt-0.5 text-xs text-content-secondary dark:text-content-secondary-dark italic">
            No search results found.
          </p>
        )}
    </div>
  );
};
