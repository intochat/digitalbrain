import type { DropdownOption } from 'shared/ui';

export interface CurrencyResponse {
  currencyCode: string;
  currencyName: string;
}

const explicitRegionByCurrency: Record<string, string> = {
  ANG: 'SX',
  AUD: 'AU',
  BAM: 'BA',
  BBD: 'BB',
  BDT: 'BD',
  BGN: 'BG',
  BHD: 'BH',
  BND: 'BN',
  BOB: 'BO',
  BRL: 'BR',
  BSD: 'BS',
  BWP: 'BW',
  BZD: 'BZ',
  CAD: 'CA',
  CHF: 'CH',
  CLP: 'CL',
  CNY: 'CN',
  COP: 'CO',
  CRC: 'CR',
  CZK: 'CZ',
  DJF: 'DJ',
  DKK: 'DK',
  DOP: 'DO',
  DZD: 'DZ',
  EGP: 'EG',
  ETB: 'ET',
  EUR: 'EU',
  FJD: 'FJ',
  GBP: 'GB',
  GEL: 'GE',
  GHS: 'GH',
  GMD: 'GM',
  GTQ: 'GT',
  HKD: 'HK',
  HNL: 'HN',
  HUF: 'HU',
  IDR: 'ID',
  ILS: 'IL',
  INR: 'IN',
  ISK: 'IS',
  JMD: 'JM',
  JOD: 'JO',
  JPY: 'JP',
  KES: 'KE',
  KGS: 'KG',
  KHR: 'KH',
  KRW: 'KR',
  KWD: 'KW',
  KZT: 'KZ',
  LAK: 'LA',
  LBP: 'LB',
  LKR: 'LK',
  MAD: 'MA',
  MDL: 'MD',
  MGA: 'MG',
  MKD: 'MK',
  MMK: 'MM',
  MNT: 'MN',
  MOP: 'MO',
  MUR: 'MU',
  MVR: 'MV',
  MWK: 'MW',
  MXN: 'MX',
  MYR: 'MY',
  MZN: 'MZ',
  NAD: 'NA',
  NGN: 'NG',
  NIO: 'NI',
  NOK: 'NO',
  NPR: 'NP',
  NZD: 'NZ',
  OMR: 'OM',
  PAB: 'PA',
  PEN: 'PE',
  PGK: 'PG',
  PHP: 'PH',
  PKR: 'PK',
  PLN: 'PL',
  PYG: 'PY',
  QAR: 'QA',
  RON: 'RO',
  RSD: 'RS',
  RUB: 'RU',
  RWF: 'RW',
  SAR: 'SA',
  SBD: 'SB',
  SCR: 'SC',
  SEK: 'SE',
  SGD: 'SG',
  SLE: 'SL',
  SOS: 'SO',
  SRD: 'SR',
  SYP: 'SY',
  THB: 'TH',
  TJS: 'TJ',
  TMT: 'TM',
  TND: 'TN',
  TOP: 'TO',
  TRY: 'TR',
  TTD: 'TT',
  TWD: 'TW',
  TZS: 'TZ',
  UAH: 'UA',
  UGX: 'UG',
  USD: 'US',
  UYU: 'UY',
  UZS: 'UZ',
  VND: 'VN',
  VUV: 'VU',
  WST: 'WS',
  XAF: 'CM',
  XCD: 'AG',
  XOF: 'SN',
  XPF: 'PF',
  YER: 'YE',
  ZAR: 'ZA',
  ZMW: 'ZM',
};

const normalizeSearchText = (value: string) =>
  value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase();

const getDisplayName = (locale: string, type: 'currency' | 'region') => {
  try {
    return new Intl.DisplayNames([locale], { type });
  } catch {
    return null;
  }
};

const resolveRegionCode = (currencyCode: string) => {
  const explicitRegionCode = explicitRegionByCurrency[currencyCode];
  if (explicitRegionCode) {
    return explicitRegionCode;
  }

  const guessedRegionCode = currencyCode.slice(0, 2);
  return /^[A-Z]{2}$/.test(guessedRegionCode) ? guessedRegionCode : null;
};

const toFlagEmoji = (regionCode: string | null) => {
  if (!regionCode || !/^[A-Z]{2}$/.test(regionCode)) {
    return '💱';
  }

  return String.fromCodePoint(...regionCode.split('').map(char => 127397 + char.charCodeAt(0)));
};

const buildCurrencyLabel = (currencyCode: string, localizedName: string, fallbackName: string) => {
  const primaryName = localizedName && localizedName !== currencyCode ? localizedName : fallbackName;
  return `${currencyCode} (${primaryName})`;
};

export const createCurrencyOption = (currency: CurrencyResponse, language: string): DropdownOption<string> => {
  const currencyCode = currency.currencyCode.toUpperCase();
  const currencyNames = getDisplayName(language, 'currency');
  const regionNames = getDisplayName(language, 'region');
  const localizedCurrencyName = currencyNames?.of(currencyCode) ?? currency.currencyName;
  const regionCode = resolveRegionCode(currencyCode);
  const localizedRegionName = regionCode ? (regionNames?.of(regionCode) ?? regionCode) : '';
  const icon = toFlagEmoji(regionCode);

  return {
    value: currencyCode,
    label: buildCurrencyLabel(currencyCode, localizedCurrencyName, currency.currencyName),
    icon,
    searchText: normalizeSearchText(
      [currencyCode, currency.currencyName, localizedCurrencyName, localizedRegionName, regionCode ?? ''].join(' ')
    ),
  };
};
