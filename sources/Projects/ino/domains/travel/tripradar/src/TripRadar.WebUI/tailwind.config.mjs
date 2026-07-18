/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      screens: {
        xs: '475px',
        landscape: { raw: '(orientation: landscape)' },
      },
      colors: {
        primary: {
          50: '#fefce8',
          400: '#facc15', // Lighter for dark theme
          500: '#eab308',
          600: '#ca8a04',
          700: '#a16207',
        },
        secondary: {
          50: '#ecfeff',
          400: '#22d3ee', // Lighter for dark theme
          500: '#06b6d4',
          600: '#0891b2',
          700: '#0e7490',
        },
        surface: {
          DEFAULT: '#ffffff',
          // Improved dark theme surfaces with warm undertones
          dark: '#0f0f0f', // Warmer than pure black (#000000)
          'dark-secondary': '#1a1a1a', // Secondary background
          'dark-tertiary': '#242424', // Tertiary background
          accent: '#f1f5f9',
          'accent-dark': '#2a2a2a', // Cards, panels - warmer
          'accent-dark-hover': '#323232', // Hover states
        },
        content: {
          DEFAULT: '#0f172a',
          // Improved text colors with better contrast
          dark: '#f8f9fa', // Softer than pure white
          secondary: '#475569',
          'secondary-dark': '#e9ecef', // Better contrast for secondary text
          muted: '#5c6b7d',
          'muted-dark': '#adb5bd', // Improved muted text contrast
          'disabled-dark': '#6c757d', // Disabled text color
        },
        outline: {
          DEFAULT: '#cbd5e1',
          // Subtle borders for better visual separation
          dark: '#404040', // Primary borders - more visible
          secondary: '#94a3b8',
          'secondary-dark': '#2d2d2d', // Subtle borders
          'accent-dark': '#4a4a4a', // Interactive borders
        },
        interactive: {
          DEFAULT: '#e2e8f0',
          // Enhanced interactive states
          dark: '#404040', // Base interactive color
          'dark-hover': '#4a4a4a', // Hover state
          'dark-active': '#525252', // Active state
          'dark-focus': '#525252', // Focus state
          active: '#ec4899',
          'active-dark': '#06b6d4',
        },
        button: {
          DEFAULT: '#0f172a',
          // Improved button colors
          dark: '#f8f9fa', // Button background (softer white)
          text: '#ffffff',
          'text-dark': '#0f172a', // Button text on light background
          hover: '#1f2937',
          'hover-dark': '#e9ecef', // Button hover state
        },
      },
      fontFamily: {
        sans: [
          'Inter',
          'ui-sans-serif',
          'system-ui',
          '-apple-system',
          'BlinkMacSystemFont',
          'Segoe UI',
          'Roboto',
          'Helvetica Neue',
          'Arial',
          'Noto Sans',
          'sans-serif',
        ],
      },
      lineHeight: {
        heading: '1.3',
      },
    },
  },
  plugins: [],
};
