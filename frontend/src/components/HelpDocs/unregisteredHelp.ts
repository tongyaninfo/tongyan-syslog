import en from '@/locales/en-US/unregistered';
import ja from '@/locales/ja-JP/unregistered';
import ko from '@/locales/ko-KR/unregistered';
import ru from '@/locales/ru-RU/unregistered';
import zh from '@/locales/zh-CN/unregistered';
import tw from '@/locales/zh-TW/unregistered';

function chapter(copy: Record<keyof typeof zh, string>) {
  return {
    title: copy['unregistered.title'],
    steps: [
      copy['unregistered.helpStep1'],
      copy['unregistered.helpStep2'],
      copy['unregistered.helpStep3'],
    ],
  };
}

export const unregisteredHelp = {
  'zh-CN': chapter(zh),
  'zh-TW': chapter(tw),
  'ja-JP': chapter(ja),
  'ko-KR': chapter(ko),
  'ru-RU': chapter(ru),
  'en-US': chapter(en),
};
