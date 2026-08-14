from aiogram import Router
from aiogram.filters import CommandStart, Command
from aiogram.types import Message

from bot.config import settings
from bot.db.crud import get_or_create_user, get_subscription, count_today_jobs
from bot.db.session import async_session

router = Router()


@router.message(CommandStart())
async def cmd_start(message: Message):
    async with async_session() as session:
        user = await get_or_create_user(
            session,
            telegram_id=message.from_user.id,
            username=message.from_user.username,
            first_name=message.from_user.first_name,
        )

    is_admin = message.from_user.id in settings.admin_ids
    role = "👑 Администратор" if is_admin else "👤 Пользователь"

    await message.answer(
        f"🦅 <b>NoirWings VM Obfuscator</b>\n\n"
        f"Добро пожаловать! {role}\n"
        f"Отправьте мне <code>.lua</code> файл для обфускации.\n\n"
        "📋 <b>Команды:</b>\n"
        "/help — справка\n"
        "/profile — выбор профиля защиты\n"
        "/options — настройка опций\n"
        "/history — история обфускаций\n"
        "/status — подписка и лимиты",
        parse_mode="HTML",
    )


@router.message(Command("help"))
async def cmd_help(message: Message):
    await message.answer(
        "🦅 <b>NoirWings VM Obfuscator — Справка</b>\n\n"
        "<b>Как использовать:</b>\n"
        "1. Отправьте <code>.lua</code> файл в чат\n"
        "2. Выберите профиль защиты\n"
        "3. Получите обфусцированный файл\n\n"
        "<b>Профили:</b>\n"
        "⚡ <b>Balanced</b> — быстрая обфускация, базовая защита\n"
        "🛡 <b>Hardened</b> — усиленная защита, opaque predicates + env cage\n"
        "🔒 <b>Maximum</b> — максимальная защита, все фичи включены\n\n"
        "<b>Лимиты:</b>\n"
        "• Бесплатно: 3 обфускации в день\n"
        "• Pro: 50 в день\n"
        "• Unlimited: без лимитов",
        parse_mode="HTML",
    )


@router.message(Command("status"))
async def cmd_status(message: Message):
    is_admin = message.from_user.id in settings.admin_ids

    async with async_session() as session:
        sub = await get_subscription(session, message.from_user.id)
        today_count = await count_today_jobs(session, message.from_user.id)

    if is_admin:
        await message.answer(
            "👑 <b>Администратор</b>\n\n"
            f"📊 Обфускаций сегодня: {today_count}\n"
            "📈 Лимит: ∞",
            parse_mode="HTML",
        )
        return

    if sub is None:
        await message.answer(
            "🆓 <b>Подписка:</b> FREE\n"
            f"📊 Обфускаций сегодня: {today_count}/3",
            parse_mode="HTML",
        )
        return

    plan_emoji = {"free": "🆓", "pro": "⭐", "unlimited": "💎"}
    expires = sub.expires_at.strftime("%d.%m.%Y") if sub.expires_at else "∞"

    await message.answer(
        f"{plan_emoji.get(sub.plan.value, '')} <b>Подписка:</b> {sub.plan.value.upper()}\n"
        f"📅 <b>Действует до:</b> {expires}\n"
        f"📊 <b>Сегодня:</b> {today_count}/{sub.daily_limit}",
        parse_mode="HTML",
    )
