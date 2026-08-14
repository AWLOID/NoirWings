from aiogram import Router
from aiogram.filters import CommandStart, Command
from aiogram.types import Message
from sqlalchemy.ext.asyncio import AsyncSession

from bot.db.crud import get_or_create_user, is_whitelisted, get_subscription
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
        whitelisted = await is_whitelisted(session, message.from_user.id)

    if not whitelisted:
        await message.answer(
            "🚫 <b>Доступ ограничен</b>\n\n"
            "У вас нет доступа к NoirWings Bot.\n"
            "Обратитесь к администратору для получения доступа.",
            parse_mode="HTML",
        )
        return

    await message.answer(
        "🦅 <b>NoirWings VM Obfuscator</b>\n\n"
        "Добро пожаловать! Отправьте мне <code>.lua</code> файл для обфускации.\n\n"
        "📋 <b>Команды:</b>\n"
        "/obfuscate — инструкция по обфускации\n"
        "/profile — выбор профиля защиты\n"
        "/options — настройка опций\n"
        "/history — история обфускаций\n"
        "/status — подписка и лимиты\n"
        "/help — справка",
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
        "<b>Опции (Luraph-tier):</b>\n"
        "• Opaque Predicates — алгебраические предикаты для dead code\n"
        "• Environment Cage — изоляция от хуков\n"
        "• Anti-Hook — проверка целостности stdlib\n"
        "• Dynamic Dispatch — переключение таблиц диспетчеризации\n"
        "• Watermark Integrity — защита от удаления вотермарки\n"
        "• String Encryption — шифрование строковых констант",
        parse_mode="HTML",
    )


@router.message(Command("status"))
async def cmd_status(message: Message):
    async with async_session() as session:
        sub = await get_subscription(session, message.from_user.id)

    if sub is None:
        await message.answer("❌ У вас нет активной подписки.")
        return

    plan_emoji = {"free": "🆓", "pro": "⭐", "unlimited": "💎"}
    expires = sub.expires_at.strftime("%d.%m.%Y") if sub.expires_at else "∞"

    await message.answer(
        f"{plan_emoji.get(sub.plan.value, '')} <b>Подписка:</b> {sub.plan.value.upper()}\n"
        f"📅 <b>Действует до:</b> {expires}\n"
        f"📊 <b>Лимит:</b> {sub.daily_limit} обф./день",
        parse_mode="HTML",
    )
