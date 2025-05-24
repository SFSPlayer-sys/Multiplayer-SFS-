# Spaceflight Simulator 多人联机版

这是 [Multiplayer-SFS](https://github.com/AstroTheRabbit/Multiplayer-SFS) 的修改版本，为 Spaceflight Simulator 添加了多人联机功能。

## 下载说明
请前往原始项目 [Multiplayer-SFS](https://github.com/AstroTheRabbit/Multiplayer-SFS) 下载 MOD。

## 原始项目
- 原始仓库: [Multiplayer-SFS](https://github.com/AstroTheRabbit/Multiplayer-SFS)
- 原始许可证: GNU GPL v3

## 修改内容
- 添加多人联机功能
- 添加服务器-客户端架构
- 添加实时同步功能
- 添加玩家互动功能

## 服务器命令
### 基础命令
- `help` - 显示所有可用命令
- `info` - 显示当前服务器配置信息
- `status` - 显示服务器运行状态和资源使用情况
- `about` - 显示服务器版本和作者信息

### 玩家管理
- `list` - 显示当前在线玩家列表
- `kick [玩家名/IP]` - 将指定玩家踢出服务器
- `ban [玩家名/IP]` - 将指定玩家加入黑名单
- `unban [玩家名/IP]` - 将指定玩家从黑名单中移除
- `blacklist` - 显示所有被封禁的玩家列表

### 世界管理
- `save [时间]` - 保存当前世界状态
  - 时间格式: 数字+单位(S=秒,M=分,H=小时,D=天)
  - 输入-1取消自动保存
- `clearall` - 清除世界中所有的火箭
- `clearshares [时间]` - 清除所有碎片
  - 时间格式同save命令

### 服务器配置
- `config [项] [值] [-n]` - 修改服务器配置
  - 可用配置项：
    - `worldSavePath`: 世界存档文件夹路径 (默认: Sav)
    - `port`: 服务器监听端口号 (默认: 9806)
    - `password`: 服务器访问密码，留空则无需密码 (默认: 空)
    - `maxConnections`: 最大同时在线玩家数 (默认: 16)
    - `blockDuplicatePlayerNames`: 是否禁止重复用户名 (默认: false)
    - `updateRocketsPeriod`: 火箭状态更新频率(毫秒) (默认: 20)
    - `loadRange`: 火箭加载范围，建议大于6000 (默认: 7500)
    - `chatMessageCooldown`: 聊天消息发送间隔(秒) (默认: 3)
    - `difficulty`: 游戏难度 (0=普通,1=困难,2=真实) (默认: 0)
  - 添加-n参数可在修改后不重启服务器

### 系统消息
- `mes [消息内容]` - 向所有在线玩家发送系统消息 