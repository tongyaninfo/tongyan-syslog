import { history } from '@umijs/max';
import { Button, Result } from 'antd';
import { useText } from '@/utils/i18n';

export default function NotFound() {
  const t = useText();
  return (
    <Result
      status="404"
      title="404"
      subTitle={t('app.notFound')}
      extra={
        <Button type="primary" onClick={() => history.replace('/overview')}>
          {t('app.back')}
        </Button>
      }
    />
  );
}
