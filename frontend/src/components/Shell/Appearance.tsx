import { useModel } from '@umijs/max';
import { ConfigProvider, Drawer, Radio, theme } from 'antd';
import { type ReactNode, useEffect } from 'react';
import { useText } from '@/utils/i18n';
import {
  isThemeMode,
  resolveTheme,
  saveThemeMode,
  subscribeSystemTheme,
} from '@/utils/theme';

const modes = [
  { value: 'auto', label: 'app.auto' },
  { value: 'light', label: 'app.light' },
  { value: 'realDark', label: 'app.dark' },
] as const;

export function useSystemAppearance() {
  const { initialState, setInitialState } = useModel('@@initialState');
  useEffect(() => {
    if (initialState?.themeMode !== 'auto') return;
    return subscribeSystemTheme((resolvedTheme) => {
      void setInitialState((state) =>
        state?.themeMode === 'auto' && state.resolvedTheme !== resolvedTheme
          ? { ...state, resolvedTheme }
          : state,
      );
    });
  }, [initialState?.themeMode, setInitialState]);
}

export function AppearanceProvider({ children }: { children: ReactNode }) {
  const { initialState } = useModel('@@initialState');
  const dark = initialState?.resolvedTheme === 'realDark';
  return (
    <ConfigProvider
      theme={{
        algorithm: dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
        token: {
          colorPrimary: dark ? '#79a8ff' : '#1849a9',
          colorLink: dark ? '#8db4ff' : '#1849a9',
        },
      }}
    >
      {children}
    </ConfigProvider>
  );
}

export function AppearanceDrawer({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const { initialState, setInitialState } = useModel('@@initialState');
  const t = useText();
  return (
    <Drawer
      title={t('app.appearance')}
      open={open}
      onClose={onClose}
      size="min(400px, 100vw)"
    >
      <h3 id="syslog-theme-style">{t('app.appearanceStyle')}</h3>
      <Radio.Group
        className="syslog-theme-options"
        aria-labelledby="syslog-theme-style"
        value={initialState?.themeMode ?? 'auto'}
        onChange={({ target }) => {
          const mode: unknown = target.value;
          if (!isThemeMode(mode)) return;
          saveThemeMode(mode);
          void setInitialState((state) => ({
            ...state,
            themeMode: mode,
            resolvedTheme: resolveTheme(mode),
          }));
        }}
      >
        {modes.map(({ value, label }) => (
          <Radio key={value} value={value}>
            <span
              className="syslog-theme-sample"
              data-mode={value}
              aria-hidden="true"
            >
              <span />
            </span>
            <span>{t(label)}</span>
          </Radio>
        ))}
      </Radio.Group>
      <p className="syslog-appearance-hint">{t('app.autoHint')}</p>
    </Drawer>
  );
}
