import type { RunTimeLayoutConfig } from '@umijs/max';
import { getIntl, history, Link } from '@umijs/max';
import {
  HeaderActions,
  PreviewBanner,
  SessionRuntime,
} from '@/components/Shell';
import SiteFooter from '@/components/Shell/SiteFooter';
import {
  loadStartupState,
  type StartupState,
  startupRedirect,
} from '@/utils/startup';
import {
  type ResolvedTheme,
  readThemeMode,
  resolveTheme,
  type ThemeMode,
} from '@/utils/theme';
import settings from '../config/defaultSettings';

export interface InitialState extends StartupState {
  bootNotice?: string;
  loginUsername?: string;
  themeMode?: ThemeMode;
  resolvedTheme?: ResolvedTheme;
}

export async function getInitialState(): Promise<InitialState> {
  const themeMode = readThemeMode();
  const state: InitialState = {
    ...(await loadStartupState()),
    themeMode,
    resolvedTheme: resolveTheme(themeMode),
  };
  const redirect = startupRedirect(state, history.location.pathname);
  if (redirect) history.replace(redirect);
  return state;
}

export const layout: RunTimeLayoutConfig = ({ initialState }) => ({
  ...settings,
  title: getIntl().formatMessage({ id: 'app.title' }),
  navTheme: initialState?.resolvedTheme ?? 'light',
  colorPrimary:
    initialState?.resolvedTheme === 'realDark' ? '#79a8ff' : '#1849a9',
  logo:
    initialState?.resolvedTheme === 'realDark'
      ? '/assets/brand/tyinfo-logo-transparent.png'
      : settings.logo,
  token: {
    sider: {
      colorTextMenu:
        initialState?.resolvedTheme === 'realDark' ? '#c4ccd8' : '#505b6d',
      colorTextMenuSelected:
        initialState?.resolvedTheme === 'realDark' ? '#ffffff' : '#1849a9',
      colorTextMenuActive:
        initialState?.resolvedTheme === 'realDark' ? '#ffffff' : '#1849a9',
    },
  },
  menuItemRender: (item, element) =>
    item.path ? <Link to={item.path}>{element}</Link> : element,
  actionsRender: () => [<HeaderActions key="syslog-actions" />],
  avatarProps: undefined,
  footerRender: () => <SiteFooter />,
  onPageChange: () => {
    const redirect = startupRedirect(
      initialState ?? {},
      history.location.pathname,
    );
    if (redirect) history.replace(redirect);
  },
  childrenRender: (children) => (
    <SessionRuntime>
      <PreviewBanner />
      {children}
    </SessionRuntime>
  ),
});
