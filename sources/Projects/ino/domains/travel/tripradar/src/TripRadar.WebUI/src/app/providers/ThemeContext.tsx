/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { THEME } from 'shared/config/constants';

type Theme = typeof THEME.LIGHT | typeof THEME.DARK;

interface ThemeContextType {
  theme: Theme;
  toggleTheme: () => void;
  setTheme: (theme: Theme) => void;
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export const ThemeProvider = ({ children }: { children: ReactNode }) => {
  const [theme, setThemeState] = useState<Theme>(() => {
    // Check localStorage first, then default to light theme
    const savedTheme = localStorage.getItem('tripradar-theme') as Theme;
    if (savedTheme && [THEME.LIGHT, THEME.DARK].includes(savedTheme)) {
      return savedTheme;
    }
    return THEME.LIGHT;
  });

  useEffect(() => {
    // Apply theme to document
    const root = document.documentElement;

    if (theme === THEME.DARK) {
      root.classList.add('dark');
      root.style.colorScheme = THEME.DARK;
    } else {
      root.classList.remove('dark');
      root.style.colorScheme = THEME.LIGHT;
    }

    // Save to localStorage
    localStorage.setItem('tripradar-theme', theme);
  }, [theme]);

  const toggleTheme = () => {
    setThemeState(prev => (prev === THEME.LIGHT ? THEME.DARK : THEME.LIGHT));
  };

  const setTheme = (newTheme: Theme) => {
    setThemeState(newTheme);
  };

  const value = {
    theme,
    toggleTheme,
    setTheme,
  };

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
};

export const useTheme = () => {
  const context = useContext(ThemeContext);
  if (context === undefined) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
};
