# Railway Deployment Guide — NoirWings

## Структура проекта на Railway

Создаётся **один проект** с **тремя сервисами**:

```
NoirWings (project)
├── postgres   — PostgreSQL база данных
├── api        — FastAPI (обфускатор API)
└── bot        — Telegram бот (aiogram)
```

---

## Шаг 1: Создай проект

1. Зайди на https://railway.app → **New Project**
2. Назови `NoirWings`

---

## Шаг 2: Добавь PostgreSQL

1. В проекте нажми **+ New** → **Database** → **PostgreSQL**
2. Railway автоматически создаст переменную `DATABASE_URL`

---

## Шаг 3: Деплой API сервиса

1. **+ New** → **GitHub Repo** → выбери `AWLOID/NoirWings`
2. В настройках сервиса:
   - **Root Directory**: `services/api`
   - **Builder**: Dockerfile
3. Перейди во вкладку **Variables**, добавь:
   ```
   API_KEY=<your-random-api-key>
   DATABASE_URL=${{Postgres.DATABASE_URL}}
   NOIRWINGS_DLL=/opt/noirwings/NoirWings.Vm.dll
   WORK_DIR=/tmp/noirwings
   ```
   > `DATABASE_URL` — используй Reference Variable к PostgreSQL сервису

4. Во вкладке **Settings** → **Networking** → **Generate Domain** (получишь URL типа `api-production-xxxx.up.railway.app`)

---

## Шаг 4: Деплой Bot сервиса

1. **+ New** → **GitHub Repo** → выбери `AWLOID/NoirWings` (тот же репо)
2. В настройках:
   - **Root Directory**: `services/bot`
   - **Builder**: Dockerfile
3. Переменные:
   ```
   BOT_TOKEN=<your-telegram-bot-token>
   API_URL=https://<твой-api-domain>.up.railway.app
   API_KEY=<your-random-api-key>
   DATABASE_URL=${{Postgres.DATABASE_URL}}
   ADMIN_IDS=[<your-telegram-id>]
   ```
   > `API_URL` — подставь домен из шага 3

---

## Шаг 5: Перед деплоем — подготовь runtime

API сервис ожидает скомпилированный `NoirWings.Vm.dll` внутри контейнера.

Нужно опубликовать движок и закоммитить артефакты:

```bash
cd engine/NoirWings.Vm
dotnet publish -c Release -r linux-x64 --self-contained false -o ../../services/api/noirwings-runtime
```

Затем:
```bash
cd services/api
git add noirwings-runtime/
git commit -m "feat: add published NoirWings.Vm runtime for Railway"
git push
```

---

## Переменные окружения (полный список)

### API сервис

| Переменная | Описание | Пример |
|---|---|---|
| `API_KEY` | Ключ авторизации между bot↔api | случайная строка 32 символа |
| `DATABASE_URL` | PostgreSQL URL (от Railway) | `${{Postgres.DATABASE_URL}}` |
| `NOIRWINGS_DLL` | Путь к .dll | `/opt/noirwings/NoirWings.Vm.dll` |
| `WORK_DIR` | Рабочая папка для job'ов | `/tmp/noirwings` |
| `PORT` | Порт (Railway задаёт автоматически) | — |

### Bot сервис

| Переменная | Описание | Пример |
|---|---|---|
| `BOT_TOKEN` | Telegram bot token | от @BotFather |
| `API_URL` | URL API сервиса на Railway | `https://api-prod-xxxx.up.railway.app` |
| `API_KEY` | Тот же ключ что у API | случайная строка 32 символа |
| `DATABASE_URL` | PostgreSQL URL | `${{Postgres.DATABASE_URL}}` |
| `ADMIN_IDS` | JSON массив Telegram ID админов | `[123456789]` |

---

## Shared Variables (общие)

В Railway можно использовать **Shared Variables** на уровне проекта:
1. Зайди в **Project Settings** → **Shared Variables**
2. Добавь `API_KEY` и `DATABASE_URL` один раз
3. В каждом сервисе используй `${{shared.API_KEY}}` и `${{shared.DATABASE_URL}}`

---

## Environments (окружения)

Railway поддерживает раздельные окружения:

- **production** — основной деплой (автоматически создаётся)
- **staging** — тестовый (для проверки перед выкатом)

Чтобы создать staging:
1. В проекте → **Environments** (иконка ветки вверху) → **New Environment**
2. Назови `staging`
3. Railway клонирует структуру, но с отдельными переменными
4. Для staging используй **другой BOT_TOKEN** (создай тестового бота через @BotFather)
5. Переменные staging не влияют на production

**Workflow:**
- `main` ветка → деплоится в `production`
- `dev` ветка → деплоится в `staging` (настрой в Settings → Source → Branch)

---

## Troubleshooting

- **Bot не стартует**: проверь `BOT_TOKEN` в Variables, смотри Deploy Logs
- **API degraded**: значит `NoirWings.Vm.dll` не найден — проверь что `noirwings-runtime/` закоммичен
- **Database connection error**: убедись что `DATABASE_URL` ссылается на `${{Postgres.DATABASE_URL}}`
- **.NET не найден**: Dockerfile использует multi-stage build с .NET 10 runtime
