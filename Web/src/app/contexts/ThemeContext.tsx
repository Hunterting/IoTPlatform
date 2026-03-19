import { createContext, useContext, useState, useEffect, ReactNode } from 'react';

export type ThemeType = 'dark' | 'light' | 'blue' | 'purple' | 'green';

interface ThemeConfig {
  id: ThemeType;
  name: string;
  colors: {
    // Background gradients
    bgPrimary: string;
    bgSecondary: string;
    bgTertiary: string;
    
    // Accent colors
    accent: string;
    accentHover: string;
    accentLight: string;
    
    // Text colors
    textPrimary: string;
    textSecondary: string;
    textTertiary: string;
    
    // Border colors
    border: string;
    borderLight: string;
  };
}

const themes: Record<ThemeType, ThemeConfig> = {
  dark: {
    id: 'dark',
    name: '深色主题',
    colors: {
      bgPrimary: 'from-gray-900 via-slate-900 to-gray-800',
      bgSecondary: 'from-gray-800/50 to-gray-900/50',
      bgTertiary: 'bg-white/5',
      accent: 'from-blue-500 to-purple-500',
      accentHover: 'from-blue-600 to-purple-600',
      accentLight: 'from-blue-500/20 to-purple-500/20',
      textPrimary: 'text-white',
      textSecondary: 'text-gray-300',
      textTertiary: 'text-gray-400',
      border: 'border-white/10',
      borderLight: 'border-white/5',
    },
  },
  light: {
    id: 'light',
    name: '浅色主题',
    colors: {
      bgPrimary: 'from-gray-50 via-white to-gray-100',
      bgSecondary: 'from-white/90 to-gray-50/90',
      bgTertiary: 'bg-gray-100',
      accent: 'from-blue-600 to-purple-600',
      accentHover: 'from-blue-700 to-purple-700',
      accentLight: 'from-blue-100 to-purple-100',
      textPrimary: 'text-gray-900',
      textSecondary: 'text-gray-700',
      textTertiary: 'text-gray-600',
      border: 'border-gray-200',
      borderLight: 'border-gray-100',
    },
  },
  blue: {
    id: 'blue',
    name: '蓝色主题',
    colors: {
      bgPrimary: 'from-blue-950 via-slate-900 to-blue-900',
      bgSecondary: 'from-blue-900/50 to-blue-950/50',
      bgTertiary: 'bg-blue-500/10',
      accent: 'from-blue-500 to-cyan-500',
      accentHover: 'from-blue-600 to-cyan-600',
      accentLight: 'from-blue-500/20 to-cyan-500/20',
      textPrimary: 'text-white',
      textSecondary: 'text-blue-100',
      textTertiary: 'text-blue-200',
      border: 'border-blue-400/20',
      borderLight: 'border-blue-400/10',
    },
  },
  purple: {
    id: 'purple',
    name: '紫色主题',
    colors: {
      bgPrimary: 'from-purple-950 via-slate-900 to-purple-900',
      bgSecondary: 'from-purple-900/50 to-purple-950/50',
      bgTertiary: 'bg-purple-500/10',
      accent: 'from-purple-500 to-pink-500',
      accentHover: 'from-purple-600 to-pink-600',
      accentLight: 'from-purple-500/20 to-pink-500/20',
      textPrimary: 'text-white',
      textSecondary: 'text-purple-100',
      textTertiary: 'text-purple-200',
      border: 'border-purple-400/20',
      borderLight: 'border-purple-400/10',
    },
  },
  green: {
    id: 'green',
    name: '绿色主题',
    colors: {
      bgPrimary: 'from-emerald-950 via-slate-900 to-emerald-900',
      bgSecondary: 'from-emerald-900/50 to-emerald-950/50',
      bgTertiary: 'bg-emerald-500/10',
      accent: 'from-emerald-500 to-teal-500',
      accentHover: 'from-emerald-600 to-teal-600',
      accentLight: 'from-emerald-500/20 to-teal-500/20',
      textPrimary: 'text-white',
      textSecondary: 'text-emerald-100',
      textTertiary: 'text-emerald-200',
      border: 'border-emerald-400/20',
      borderLight: 'border-emerald-400/10',
    },
  },
};

interface ThemeContextType {
  theme: ThemeType;
  themeConfig: ThemeConfig;
  setTheme: (theme: ThemeType) => void;
  themes: Record<ThemeType, ThemeConfig>;
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemeType>(() => {
    const saved = localStorage.getItem('theme');
    return (saved as ThemeType) || 'dark';
  });

  useEffect(() => {
    localStorage.setItem('theme', theme);
  }, [theme]);

  const setTheme = (newTheme: ThemeType) => {
    setThemeState(newTheme);
  };

  const themeConfig = themes[theme];

  return (
    <ThemeContext.Provider value={{ theme, themeConfig, setTheme, themes }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (context === undefined) {
    // 在开发环境下，热重载可能会导致context暂时为undefined
    if (import.meta.env.DEV) {
      console.warn('useTheme called outside of ThemeProvider - returning default theme during hot reload');
      return {
        theme: 'dark' as ThemeType,
        themeConfig: themes.dark,
        setTheme: () => {},
        themes,
      };
    }
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
}