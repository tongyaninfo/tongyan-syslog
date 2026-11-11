import { setLocale, useIntl } from '@umijs/max';
import dayjs from 'dayjs';
import { useCallback, useEffect } from 'react';
import 'dayjs/locale/zh-cn';
import 'dayjs/locale/zh-tw';
import 'dayjs/locale/ja';
import 'dayjs/locale/ko';
import 'dayjs/locale/ru';

export const supportedLanguages = [
  { value: 'zh-CN', label: '简体中文' },
  { value: 'zh-TW', label: '繁體中文' },
  { value: 'ja-JP', label: '日本語' },
  { value: 'ko-KR', label: '한국어' },
  { value: 'ru-RU', label: 'Русский' },
  { value: 'en-US', label: 'English' },
] as const;

type Language = (typeof supportedLanguages)[number]['value'];
const calendarLocales: Record<Language, string> = {
  'zh-CN': 'zh-cn',
  'zh-TW': 'zh-tw',
  'ja-JP': 'ja',
  'ko-KR': 'ko',
  'ru-RU': 'ru',
  'en-US': 'en',
};

export function useLanguage() {
  const { locale } = useIntl();
  useEffect(() => {
    document.documentElement.lang = locale;
    dayjs.locale(calendarLocales[locale as Language] ?? 'en');
  }, [locale]);

  return {
    locale,
    setLanguage: (language: string) => {
      // Umi 持久化显示语言，通过事件刷新文案，保留当前页面与表单。
      if (supportedLanguages.some(({ value }) => value === language))
        setLocale(language, false);
    },
  };
}

export function useText() {
  const intl = useIntl();
  return useCallback(
    (key: string, values?: Record<string, string | number>) =>
      intl.formatMessage({ id: key }, values),
    [intl],
  );
}
