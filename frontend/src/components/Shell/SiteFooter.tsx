import { theme } from 'antd';

export default function SiteFooter() {
  // 页脚链接直接使用当前主题的文字色，避免继承页面默认的黑色。
  const { token } = theme.useToken();

  return (
    <footer className="syslog-site-footer">
      <a
        href="https://www.tongyaninfo.com"
        target="_blank"
        rel="noopener noreferrer"
        style={{ color: token.colorTextSecondary }}
      >
        <img
          src="/assets/brand/tyinfo-logo-horizontal_mini.png"
          alt=""
          width={114}
          height={38}
        />
        <span>TongYanInfo Site</span>
      </a>
    </footer>
  );
}
