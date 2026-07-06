# SupabaseKeepAliveTool

> Supabase 数据库保活工具 —— 防止免费版 Supabase 项目因长期未使用而被暂停/休眠。

[English](README.en.md)

## 功能简介

Supabase 免费计划的数据库在 **7 天无活动** 后会被自动冻结。本工具是一个 Unity MonoBehaviour 脚本，可批量配置多个 Supabase 项目，定期自动登录并插入一行数据，从而保持数据库处于活跃状态。

核心特性：

- 🔁 支持同时配置**多个 Supabase 项目**，逐一登录保活
- 🖥️ 内置 **IMGUI 运行时界面**（可在游戏/应用中直接查看状态）
- ⚙️ 编辑器内可视化配置编辑，一键保存/加载 JSON 配置文件
- 📝 实时日志输出，方便排查问题
- 🔧 支持自定义保活表名、请求超时、执行间隔等参数

## 环境要求

| 依赖 | 版本 |
|------|------|
| Unity | 2021.3 LTS 及以上 |
| Odin Inspector | 3.x（用于序列化与编辑器 GUI） |

## 安装

1. 将 `Assets/Supabase/` 文件夹复制到你的 Unity 项目中
2. 确保项目已导入 [Odin Inspector](https://assetstore.unity.com/packages/tools/utilities/odin-inspector-and-serializer-89041) 插件
3. 将 `SupabaseKeepAliveTool` 脚本挂载到场景中的任意 GameObject 上

## 使用说明

### 1. 准备配置文件

在 `Assets/StreamingAssets/` 目录下创建 `supabase_keepalive.json`，格式如下：

```json
{
  "email": "your-email@example.com",
  "password": "your-password",
  "timeoutSec": 8,
  "intervalSeconds": 0.2,
  "targets": [
    {
      "name": "项目A",
      "url": "https://xxxx.supabase.co",
      "apikey": "eyJhbGciOiJIUzI1NiIs..."
    },
    {
      "name": "项目B",
      "url": "https://yyyy.supabase.co",
      "apikey": "eyJhbGciOiJIUzI1NiIs..."
    }
  ]
}
```

> **获取 Supabase URL 和 API Key**：进入 [Supabase Dashboard](https://supabase.com/dashboard) → 选择项目 → Settings → API。

### 2. 组件属性说明

| 属性 | 说明 |
|------|------|
| `configFileName` | 配置文件名，默认 `supabase_keepalive.json` |
| `runOnStart` | 启动时自动执行保活 |
| `logVerbose` | 输出详细日志 |
| `showGuiText` | 显示运行时 GUI 界面 |
| `guiFontSize` | GUI 字体大小 |
| `keepAliveTableName` | 保活写入的目标表名，默认 `test` |

### 3. 保活原理

工具对每个 target 执行以下操作：

1. 调用 `/auth/v1/token?grant_type=password` 接口完成登录，获取 `access_token`
2. 调用 `/rest/v1/{table}` 接口向指定表插入一行 `{ created_at, change_time }` 记录
3. 逐个执行，间隔可配置

### 4. 目标表结构

保活表只需包含以下字段（使用 Supabase 默认的 `uuid` 主键即可）：

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | uuid | 主键（自动生成） |
| `created_at` | timestamptz | 创建时间 |
| `change_time` | timestamptz | 变更时间 |

你可以在 Supabase 的 SQL Editor 中执行：

```sql
CREATE TABLE IF NOT EXISTS test (
  id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
  created_at timestamptz DEFAULT now(),
  change_time timestamptz DEFAULT now()
);
```

## 安全提示

> ⚠️ **配置文件包含 Supabase 登录凭据和 API Key，请勿提交到公开仓库！**

本项目的 `.gitignore` 已配置忽略 `supabase_keepalive.json`。如果你需要额外的安全措施：

- 使用环境变量或加密存储凭据
- 为保活用途创建一个权限最小化的专用 Supabase 账号
- 定期轮换 API Key

## 目录结构

```
SupabaseKeepAliveTool/
├── Assets/
│   ├── Supabase/
│   │   └── Tools/
│   │       └── SupabaseKeepAliveTool.cs   # 核心脚本
│   ├── StreamingAssets/
│   │   └── supabase_keepalive.json        # 配置文件（需自行创建）
│   └── Scenes/
│       └── SampleScene.unity
├── README.md
├── README.en.md
└── .gitignore
```

## 许可证

MIT License
