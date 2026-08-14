# NoirWings Telegram Bot + API

## Быстрый старт

### 1. Подготовка

```bash
cd services
cp .env.example .env
# Отредактируйте .env: вставьте BOT_TOKEN от @BotFather и сгенерируйте API_KEY
```

### 2. Публикация NoirWings.Vm

```bash
cd ../engine/NoirWings.Vm
dotnet publish -c Release -o ../../services/noirwings-runtime
```

### 3. Запуск

```bash
cd services
docker-compose up -d
```

### 4. Добавить себя в whitelist

Укажите свой Telegram ID в `ADMIN_IDS` в `.env`. Админы автоматически проходят whitelist.

Для добавления других пользователей используйте команду:
```
/whitelist add <telegram_id>
```

## Архитектура

```
┌─────────────────┐     HTTP     ┌─────────────────┐     subprocess     ┌─────────────────┐
│  Telegram Bot   │ ──────────── │   FastAPI (api)  │ ─────────────────── │  NoirWings.Vm   │
│  (aiogram 3.x)  │              │   :8000          │                     │  (.NET 10)      │
└────────┬────────┘              └────────┬────────┘                     └─────────────────┘
         │                                │
         └──────── PostgreSQL ────────────┘
```

## Команды бота

| Команда | Описание |
|---------|----------|
| `/start` | Приветствие, проверка доступа |
| `/help` | Справка по использованию |
| `/profile` | Выбор профиля по умолчанию |
| `/options` | Тонкая настройка опций |
| `/history` | История обфускаций |
| `/status` | Подписка и лимиты |
| `/admin` | Панель администратора |
| `/whitelist add/remove <id>` | Управление whitelist |
| `/grant <id> <plan> <days>` | Выдать подписку |
| `/stats` | Статистика |

## Профили

- **balanced** — быстрая обфускация, базовая защита
- **hardened** — opaque predicates, environment cage, anti-hook
- **maximum** — все фичи, включая dynamic dispatch

## Подписки

| План | Лимит/день | Описание |
|------|-----------|----------|
| free | 5 | Базовый доступ |
| pro | 50 | Расширенный |
| unlimited | ∞ | Без лимитов |
