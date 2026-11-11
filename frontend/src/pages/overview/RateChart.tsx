import { useIntl } from '@umijs/max';
import { Card, Typography, theme } from 'antd';
import { type PointerEvent, type ReactNode, useMemo, useState } from 'react';
import { formatCount, formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import {
  MAX_RATE_SAMPLE_GAP_MS,
  RATE_WINDOW_SECONDS,
  type RatePoint,
} from './rateHistory';
import styles from './styles.module.less';

const width = 600;
const height = 160;
const divisions = [0, 1, 2, 3, 4];
const windowMs = RATE_WINDOW_SECONDS * 1000;

const timeRatio = (at: number, end: number) => 1 - (end - at) / windowMs;
const rateRatio = (rate: number, scale: number) => 1 - rate / scale;
const clampRatio = (value: number) => Math.max(0, Math.min(1, value));

function nearestSample(history: RatePoint[], at: number) {
  let nearest: RatePoint | undefined;
  // 不跨采样缺口把远处的值当成鼠标位置的真实数据。
  let distance = MAX_RATE_SAMPLE_GAP_MS / 2;
  for (const point of history) {
    const offset = Math.abs(point.at - at);
    if (offset <= distance) {
      nearest = point;
      distance = offset;
    }
  }
  return nearest;
}

function axisStep(peak: number): number {
  const raw = Math.max(peak / 4, 1);
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  return (
    magnitude * ([1, 2, 2.5, 5, 10].find((n) => n >= raw / magnitude) ?? 10)
  );
}

function linePaths(history: RatePoint[], end: number, scale: number) {
  const left = end - windowMs;
  const series = (key: 'received' | 'written' | 'ignored') => {
    let drawing = false;
    return history
      .map((sample) => {
        const rate = sample[key];
        if (sample.at < left || rate === null) {
          drawing = false;
          return '';
        }
        const x = timeRatio(sample.at, end) * width;
        const y = rateRatio(rate, scale) * height;
        const point = `${x.toFixed(2)} ${y.toFixed(2)}`;
        if (drawing) return `L${point}`;
        drawing = true;
        // 缺样后从新的起点开始；独立首点用圆头短线显示。
        return `M${point}h0.01`;
      })
      .join('');
  };
  return {
    received: series('received'),
    written: series('written'),
    ignored: series('ignored'),
  };
}

export default function RateChart({
  history,
  live,
  sourceFilter,
}: {
  history: RatePoint[];
  live: boolean;
  sourceFilter: ReactNode;
}) {
  const t = useText();
  const { locale } = useIntl();
  const { token } = theme.useToken();
  const [pointer, setPointer] = useState<{ x: number; y: number }>();
  const latest = history.at(-1);
  const end = latest?.at ?? 0;
  const receivedColor = token.colorPrimary;
  const writtenColor = token.colorSuccessText;
  const ignoredColor = token.colorWarningText;
  const series = [
    { key: 'received', color: receivedColor, dashed: true },
    { key: 'written', color: writtenColor, dashed: false },
    { key: 'ignored', color: ignoredColor, dashed: true },
  ] as const;
  const { step, received, written, ignored } = useMemo(() => {
    const peak = history.reduce(
      (max, sample) =>
        Math.max(
          max,
          sample.received ?? 0,
          sample.written ?? 0,
          sample.ignored ?? 0,
        ),
      0,
    );
    const step = axisStep(peak);
    return { step, ...linePaths(history, end, step * 4) };
  }, [history, end]);
  const sample =
    pointer && nearestSample(history, end - windowMs * (1 - pointer.x));
  const crossX = sample ? timeRatio(sample.at, end) : pointer?.x;
  const hasRate = sample && series.some(({ key }) => sample[key] != null);
  const movePointer = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType === 'touch') return;
    const plot = event.currentTarget.getBoundingClientRect();
    if (!plot.width || !plot.height) return;
    setPointer({
      x: clampRatio((event.clientX - plot.left) / plot.width),
      y: clampRatio((event.clientY - plot.top) / plot.height),
    });
  };
  const axisNumber = useMemo(
    () =>
      new Intl.NumberFormat(locale, {
        notation: 'compact',
        maximumFractionDigits: 1,
      }),
    [locale],
  );
  const axisTime = useMemo(
    () =>
      new Intl.NumberFormat(locale, {
        style: 'unit',
        unit: 'second',
        unitDisplay: 'narrow',
      }),
    [locale],
  );

  return (
    <Card
      className={styles.rateCard}
      title={t('overview.rate.title')}
      extra={
        <div className={styles.rateControls}>
          <Typography.Text type="secondary">
            {t('overview.rate.window', { seconds: RATE_WINDOW_SECONDS })}
          </Typography.Text>
          {sourceFilter}
        </div>
      }
    >
      <dl className={styles.rateLegend} aria-label={t('overview.rate.latest')}>
        {series.map(({ key, color, dashed }) => {
          const rate = live ? latest?.[key] : null;
          return (
            <div className={styles.rateLegendItem} key={key}>
              <dt>
                <span
                  aria-hidden
                  className={styles.rateSwatch}
                  style={{
                    borderColor: color,
                    borderTopStyle: dashed ? 'dashed' : 'solid',
                  }}
                />
                <Typography.Text type="secondary">
                  {t(`overview.rate.${key}`)}
                </Typography.Text>
              </dt>
              <dd>
                <Typography.Text strong className={styles.rateValue}>
                  {formatCount(rate, locale)}
                </Typography.Text>
                {rate != null && (
                  <Typography.Text type="secondary">
                    {t('overview.rate.unit')}
                  </Typography.Text>
                )}
              </dd>
            </div>
          );
        })}
      </dl>
      <div
        className={styles.rateAxes}
        style={{ color: token.colorTextSecondary }}
      >
        <div className={styles.rateTicksY} aria-hidden>
          {divisions.map((index) => (
            <span key={index} style={{ top: `${index * 25}%` }}>
              {axisNumber.format((4 - index) * step)}
            </span>
          ))}
        </div>
        <div
          className={styles.ratePlot}
          onPointerMove={movePointer}
          onPointerLeave={() => setPointer(undefined)}
          onPointerCancel={() => setPointer(undefined)}
        >
          <svg
            role="img"
            className={styles.rateSvg}
            viewBox={`0 0 ${width} ${height}`}
            preserveAspectRatio="none"
          >
            <title>{t('overview.rate.chartLabel')}</title>
            <g stroke={token.colorSplit} aria-hidden>
              {divisions.map((index) => {
                const x = (width / 4) * index;
                const y = (height / 4) * index;
                return (
                  <path
                    key={index}
                    d={`M0 ${y}H${width} M${x} 0V${height}`}
                    vectorEffect="non-scaling-stroke"
                  />
                );
              })}
            </g>
            <g fill="none" strokeLinecap="round" strokeLinejoin="round">
              <path
                d={written}
                stroke={writtenColor}
                strokeWidth="3"
                vectorEffect="non-scaling-stroke"
              />
              <path
                d={received}
                stroke={receivedColor}
                strokeWidth="2"
                strokeDasharray="5 4"
                vectorEffect="non-scaling-stroke"
              />
              <path
                d={ignored}
                stroke={ignoredColor}
                strokeWidth="2"
                strokeDasharray="2 4"
                vectorEffect="non-scaling-stroke"
              />
            </g>
            {pointer && crossX !== undefined && (
              <path
                aria-hidden
                d={`M${crossX * width} 0V${height} M0 ${pointer.y * height}H${width}`}
                fill="none"
                stroke={token.colorTextTertiary}
                strokeDasharray="4 4"
                vectorEffect="non-scaling-stroke"
              />
            )}
          </svg>
          {!received && !written && !ignored && (
            <Typography.Text type="secondary" className={styles.rateWaiting}>
              {t('overview.rate.waiting')}
            </Typography.Text>
          )}
          {pointer && crossX !== undefined && (
            <>
              {sample &&
                [...series].reverse().map(({ key, color }) => {
                  const rate = sample[key];
                  if (rate == null) return null;
                  const diameter =
                    key === 'ignored' ? 13 : key === 'written' ? 10 : 7;
                  return (
                    <span
                      aria-hidden
                      className={styles.rateMarker}
                      key={key}
                      style={{
                        left: `${crossX * 100}%`,
                        top: `${rateRatio(rate, step * 4) * 100}%`,
                        background: color,
                        borderColor: token.colorBgContainer,
                        width: diameter,
                        height: diameter,
                      }}
                    />
                  );
                })}
              <div
                role="tooltip"
                className={styles.rateTooltip}
                data-side={crossX >= 0.5 ? 'left' : 'right'}
                style={{
                  left: `${crossX * 100}%`,
                  top: pointer.y >= 0.5 ? 8 : undefined,
                  bottom: pointer.y < 0.5 ? 8 : undefined,
                  background: token.colorBgElevated,
                  borderColor: token.colorBorderSecondary,
                  boxShadow: token.boxShadowSecondary,
                  color: token.colorText,
                }}
              >
                {sample && (
                  <div className={styles.rateTooltipTime}>
                    <span>{t('overview.rate.sampleTime')}</span>
                    <time dateTime={new Date(sample.at).toISOString()}>
                      {formatTime(sample.at).slice(11)}
                    </time>
                  </div>
                )}
                {hasRate ? (
                  <dl className={styles.rateTooltipValues}>
                    {series.map(({ key, color }) => (
                      <div key={key}>
                        <dt>
                          <span
                            aria-hidden
                            className={styles.rateTooltipDot}
                            style={{ background: color }}
                          />
                          {t(`overview.rate.${key}`)}
                        </dt>
                        <dd>
                          {formatCount(sample?.[key], locale)}
                          {sample?.[key] != null &&
                            ` ${t('overview.rate.unit')}`}
                        </dd>
                      </div>
                    ))}
                  </dl>
                ) : (
                  <div>{t('overview.rate.noSample')}</div>
                )}
              </div>
            </>
          )}
        </div>
        <div className={styles.rateTicksX} aria-hidden>
          {divisions.map((index) => (
            <span key={index} style={{ left: `${index * 25}%` }}>
              {axisTime.format(((index - 4) * RATE_WINDOW_SECONDS) / 4)}
            </span>
          ))}
        </div>
      </div>
    </Card>
  );
}
