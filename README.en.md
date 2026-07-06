# SupabaseKeepAliveTool

> A Supabase database keep-alive tool — prevents free-tier Supabase projects from being paused due to inactivity.

[中文](README.md)

## Overview

Supabase's free plan suspends databases after **7 days of inactivity**. This Unity MonoBehaviour tool can be configured with multiple Supabase projects and periodically signs in + inserts a row to keep each database alive.

Key features:

- 🔁 Supports **multiple Supabase projects** in a single configuration
- 🖥️ Built-in **IMGUI runtime overlay** (view status directly in-game)
- ⚙️ Visual config editor in Unity Inspector — save/load JSON config with one click
- 📝 Real-time log output for easy debugging
- 🔧 Customizable table name, request timeout, execution interval, etc.

## Requirements

| Dependency | Version |
|-----------|---------|
| Unity | 2021.3 LTS or later |
| Odin Inspector | 3.x (for serialization & editor GUI) |

## Installation

1. Copy the `Assets/Supabase/` folder into your Unity project
2. Ensure [Odin Inspector](https://assetstore.unity.com/packages/tools/utilities/odin-inspector-and-serializer-89041) is imported
3. Attach the `SupabaseKeepAliveTool` component to any GameObject in your scene

## Usage

### 1. Prepare Configuration File

Create `supabase_keepalive.json` in `Assets/StreamingAssets/`:

```json
{
  "email": "your-email@example.com",
  "password": "your-password",
  "timeoutSec": 8,
  "intervalSeconds": 0.2,
  "targets": [
    {
      "name": "Project A",
      "url": "https://xxxx.supabase.co",
      "apikey": "eyJhbGciOiJIUzI1NiIs..."
    },
    {
      "name": "Project B",
      "url": "https://yyyy.supabase.co",
      "apikey": "eyJhbGciOiJIUzI1NiIs..."
    }
  ]
}
```

> **Get Supabase URL & API Key**: Go to [Supabase Dashboard](https://supabase.com/dashboard) → Select project → Settings → API.

### 2. Component Properties

| Property | Description |
|----------|-------------|
| `configFileName` | Config file name (default: `supabase_keepalive.json`) |
| `runOnStart` | Auto-run keep-alive on Start |
| `logVerbose` | Verbose logging |
| `showGuiText` | Show runtime GUI overlay |
| `guiFontSize` | GUI font size |
| `keepAliveTableName` | Target table name for keep-alive inserts (default: `test`) |

### 3. How It Works

For each configured target, the tool:

1. Calls `/auth/v1/token?grant_type=password` to sign in and obtain an `access_token`
2. Calls `/rest/v1/{table}` to insert a `{ created_at, change_time }` row
3. Processes targets sequentially with a configurable interval

### 4. Target Table Schema

The keep-alive table needs these fields (Supabase's default `uuid` primary key is fine):

| Column | Type | Description |
|--------|------|-------------|
| `id` | uuid | Primary key (auto-generated) |
| `created_at` | timestamptz | Creation timestamp |
| `change_time` | timestamptz | Change timestamp |

Run this in Supabase SQL Editor:

```sql
CREATE TABLE IF NOT EXISTS test (
  id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
  created_at timestamptz DEFAULT now(),
  change_time timestamptz DEFAULT now()
);
```

## Security Notice

> ⚠️ **The config file contains Supabase credentials and API Keys. Do NOT commit it to a public repository!**

This project's `.gitignore` is configured to ignore `supabase_keepalive.json`. For additional security:

- Use environment variables or encrypted credential storage
- Create a dedicated Supabase account with minimal permissions for keep-alive purposes
- Rotate API Keys periodically

## Project Structure

```
SupabaseKeepAliveTool/
├── Assets/
│   ├── Supabase/
│   │   └── Tools/
│   │       └── SupabaseKeepAliveTool.cs   # Core script
│   ├── StreamingAssets/
│   │   └── supabase_keepalive.json        # Config (create manually)
│   └── Scenes/
│       └── SampleScene.unity
├── README.md
├── README.en.md
└── .gitignore
```

## License

MIT License
