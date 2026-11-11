import { PageContainer } from '@ant-design/pro-components';
import { Tabs } from 'antd';
import { useState } from 'react';
import { useText } from '@/utils/i18n';
import DeliveryHistory from './DeliveryHistory';
import ReportSubscriptions from './ReportSubscriptions';
import SmtpSettings from './SmtpSettings';

export default function NotificationsPage() {
  const t = useText();
  const [tab, setTab] = useState('smtp');
  const [deliveryId, setDeliveryId] = useState<string>();

  return (
    <PageContainer
      title={t('menu.notifications')}
      content={t('notifications.description')}
    >
      <Tabs
        activeKey={tab}
        onChange={setTab}
        items={[
          {
            key: 'smtp',
            label: t('notifications.smtp'),
            children: (
              <SmtpSettings
                onViewDelivery={(id) => {
                  setDeliveryId(id);
                  setTab('history');
                }}
              />
            ),
          },
          {
            key: 'reports',
            label: t('notifications.reports'),
            children: <ReportSubscriptions />,
          },
          {
            key: 'history',
            label: t('notifications.history'),
            children: (
              <DeliveryHistory
                deliveryId={deliveryId}
                onViewDelivery={setDeliveryId}
              />
            ),
          },
        ]}
      />
    </PageContainer>
  );
}
