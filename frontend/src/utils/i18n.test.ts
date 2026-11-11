import { describe, expect, it } from 'vitest';
import enUS from '@/locales/en-US';
import jaJP from '@/locales/ja-JP';
import koKR from '@/locales/ko-KR';
import ruRU from '@/locales/ru-RU';
import zhCN from '@/locales/zh-CN';
import zhTW from '@/locales/zh-TW';

const parameters = (message: string) =>
  [...message.matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort();

describe('六语界面文案', () => {
  it.each([
    ['zh-TW', zhTW],
    ['ja-JP', jaJP],
    ['ko-KR', koKR],
    ['ru-RU', ruRU],
    ['en-US', enUS],
  ] as const)('%s 保留所有插值字段', (_locale, messages) => {
    for (const key of Object.keys(zhCN) as Array<keyof typeof zhCN>) {
      expect(messages[key].trim(), key).not.toBe('');
      expect(parameters(messages[key]), key).toEqual(parameters(zhCN[key]));
    }
  });
});
