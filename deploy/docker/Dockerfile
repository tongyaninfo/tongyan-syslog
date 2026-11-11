# 通研 Syslog 管理宿主镜像（linux/amd64）
#
# 构建上下文是「发布产物目录」，镜像内只复制文件、不执行还原或编译，
# 因此不需要在容器里安装 .NET SDK / Node.js，也能在 arm64 开发机上交叉构建 amd64 镜像。
#
# 前置步骤（仓库根执行）：
#   cd frontend && npm run build:backend          # 前端产物复制到 backend/src/Syslog.Api/wwwroot
#   dotnet publish backend/src/Syslog.Api/Syslog.Api.csproj -c Release -r linux-x64 \
#     --self-contained false -o /tmp/syslog-publish
#   cp deploy/docker/Dockerfile /tmp/syslog-publish/
#   docker build --platform linux/amd64 -t <harbor>/syslog/api:<tag> /tmp/syslog-publish
#
# 运行期数据（SQLite 控制库、接收暂存）与证书由 Compose 绑定宿主目录，不进入镜像层。
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Notifications__KeyDirectory 仅用于读取上一版本已保存的授权码密文。
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    HttpsCertificate__Directory=/app/data/control/https-certificate \
    Notifications__KeyDirectory=/app/data/control/notification-keys \
    TZ=Asia/Shanghai

WORKDIR /app
COPY . /app/

# 管理 HTTPS；设备接收端口实际绑定由监听配置决定，这里只声明意图。
EXPOSE 7443/tcp
EXPOSE 514/udp 514/tcp 6514/tcp 2514/tcp

# 非 root 运行；低端口（514）需要 Compose 中的
# sysctls: net.ipv4.ip_unprivileged_port_start=0 才可绑定。
USER app
ENTRYPOINT ["dotnet", "Syslog.Api.dll"]
