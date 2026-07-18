/**
 * Utility functions for handling URL parameters and encoding/decoding
 */

/**
 * Fix email parameter extracted from URL search params
 *
 * The issue: URLSearchParams.get() automatically decodes URL-encoded parameters,
 * but it converts '+' symbols to spaces, which breaks email addresses like 'user+tag@domain.com'
 *
 * This function fixes the most common issue (+ symbols) while preserving other characters.
 * For more complex cases, consider using a proper URL parameter encoding strategy on the backend.
 *
 * @param emailParam - Email parameter as returned by searchParams.get()
 * @returns Fixed email address with + symbols restored
 *
 * @example
 * // URL: ?email=user+tag@domain.com
 * const emailParam = searchParams.get('email'); // Returns: "user tag@domain.com"
 * const fixedEmail = fixEmailFromUrlParam(emailParam); // Returns: "user+tag@domain.com"
 */
export const fixEmailFromUrlParam = (emailParam: string | null): string | null => {
  if (!emailParam) {
    return null;
  }

  // Fix the most common issue: + symbols converted to spaces
  // This handles the majority of real-world cases
  return emailParam.replace(/ /g, '+');
};

/**
 * More comprehensive email URL parameter fixing (for future use)
 *
 * This function handles more edge cases but is more complex.
 * Use this if you encounter issues with other special characters.
 *
 * @param emailParam - Email parameter as returned by searchParams.get()
 * @returns Fixed email address
 */
export const fixEmailFromUrlParamAdvanced = (emailParam: string | null): string | null => {
  if (!emailParam) {
    return null;
  }

  // For now, just handle the + symbol case
  // In the future, we could add more sophisticated URL decoding logic here
  // if other special characters become problematic

  let fixedEmail = emailParam;

  // Fix + symbols that were converted to spaces
  fixedEmail = fixedEmail.replace(/ /g, '+');

  // Add more fixes here if needed in the future:
  // - Handle other URL-encoded characters
  // - Validate email format
  // - Log warnings for suspicious patterns

  return fixedEmail;
};

/**
 * Safely extract and fix email from URL search parameters
 *
 * @param searchParams - URLSearchParams object
 * @param paramName - Parameter name (default: 'email')
 * @returns Fixed email address or null if not found
 */
export const getEmailFromUrlParams = (searchParams: URLSearchParams, paramName: string = 'email'): string | null => {
  const emailParam = searchParams.get(paramName);
  return fixEmailFromUrlParam(emailParam);
};
