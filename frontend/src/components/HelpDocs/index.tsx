import { BookOutlined } from '@ant-design/icons';
import { useIntl } from '@umijs/max';
import { Button, Collapse, Drawer, Image, Tooltip, Typography } from 'antd';
import { useState } from 'react';
import { chapterImages, helpCopy } from './content';
import styles from './styles.module.less';

const chapterOrder = [
  'setup',
  'account',
  'overview',
  'search',
  'unregistered',
  'sources',
  'retention',
  'users',
] as const;

export default function HelpDocs() {
  const { locale } = useIntl();
  const [open, setOpen] = useState(false);
  const copy = helpCopy[locale as keyof typeof helpCopy] ?? helpCopy['zh-CN'];

  return (
    <>
      <Tooltip title={copy.title}>
        <Button
          type="text"
          icon={<BookOutlined />}
          aria-label={copy.title}
          onClick={() => setOpen(true)}
        />
      </Tooltip>
      <Drawer
        title={copy.title}
        open={open}
        size="min(860px, 100vw)"
        onClose={() => setOpen(false)}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">
          {copy.screenshotNote}
        </Typography.Paragraph>
        <Collapse
          defaultActiveKey={['overview']}
          items={chapterOrder.map((key) => {
            const chapter = copy.chapters[key];
            return {
              key,
              label: chapter.title,
              children: (
                <>
                  <ol className={styles.steps}>
                    {chapter.steps.map((step) => (
                      <li key={step}>{step}</li>
                    ))}
                  </ol>
                  {/* 文档图片来自合成预览，仅在展开相关章节时加载。 */}
                  {chapterImages[key].map((image) => (
                    <figure className={styles.figure} key={image}>
                      <Image
                        className={styles.image}
                        width="100%"
                        src={`/assets/help/${image}.jpg`}
                        alt={copy.captions[image]}
                        loading="lazy"
                      />
                      <figcaption>
                        <Typography.Text type="secondary">
                          {copy.captions[image]}
                        </Typography.Text>
                      </figcaption>
                    </figure>
                  ))}
                </>
              ),
            };
          })}
        />
      </Drawer>
    </>
  );
}
