interface JWTPayload {
  sub?: string; // subject (обычно user ID)
  unique_name?: string; // JwtRegisteredClaimNames.Name
  name?: string; // имя пользователя
  username?: string; // username
  email?: string; // email пользователя
  iat?: number; // issued at
  exp?: number; // expires at
  [key: string]: unknown; // другие поля
}

/**
 * Декодирует JWT токен без проверки подписи (только для чтения данных)
 */
export const decodeJWT = (token: string): JWTPayload | null => {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) {
      return null;
    }

    const base64Url = parts[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(jsonPayload) as JWTPayload;
  } catch {
    return null;
  }
};

/**
 * Получает username из JWT токена
 */
export const getUsernameFromToken = (token: string): string | null => {
  const payload = decodeJWT(token);
  return payload?.unique_name || payload?.name || payload?.username || payload?.email || payload?.sub || null;
};
