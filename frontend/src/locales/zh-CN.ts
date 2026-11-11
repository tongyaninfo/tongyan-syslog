import common from './zh-CN/common';
import httpsCertificate from './zh-CN/httpsCertificate';
import listeners from './zh-CN/listeners';
import liveLogs from './zh-CN/liveLogs';
import logs from './zh-CN/logs';
import mcp from './zh-CN/mcp';
import menu from './zh-CN/menu';
import notifications from './zh-CN/notifications';
import overview from './zh-CN/overview';
import sources from './zh-CN/sources';
import unregistered from './zh-CN/unregistered';
import users from './zh-CN/users';

export default {
  ...liveLogs,
  ...mcp,
  ...notifications,
  ...httpsCertificate,
  ...listeners,
  ...unregistered,
  ...common,
  ...menu,
  ...logs,
  ...overview,
  ...sources,
  ...users,
};
