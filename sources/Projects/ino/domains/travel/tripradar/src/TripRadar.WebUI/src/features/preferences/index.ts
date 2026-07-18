// UI Components
export { SelectField, NumberField, ToggleField, PreferenceGroup, UserPreferencesSection } from './ui';

export type {
  SelectFieldProps,
  SelectFieldOption,
  NumberFieldProps,
  ToggleFieldProps,
  PreferenceGroupProps,
  UserPreferencesSectionProps,
} from './ui';

// Form Management
export { usePreferencesForm } from './lib/usePreferencesForm';
export type { PreferencesFormState, PreferencesFormAction, UsePreferencesFormOptions } from './lib/usePreferencesForm';

// Category Configuration
export {
  PREFERENCE_CATEGORIES,
  getCategoryById,
  getPreferenceCategoriesByKeys,
  getPreferenceGroupByKey,
  getAllPreferenceKeys,
  mapServiceTypeNameToPreferenceKey,
  mapServiceTypeNamesToPreferenceKeys,
} from './lib/categoryConfig';
export type { PreferenceCategory, PreferenceGroupConfig } from './lib/categoryConfig';
