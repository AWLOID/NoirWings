from aiogram import F, Router
from aiogram.filters import Command
from aiogram.types import CallbackQuery, Message

from bot.config import settings
from bot.db.crud import (
    add_to_whitelist,
    get_total_stats,
    grant_subscription,
    remove_from_whitelist,
)
from bot.db.models import Plan
from bot.db.session import async_session
from bot.keyboards.admin import admin_keyboard

router = Router()


def is_admin(user_id: int) -> bool:
    return user_id in settings.admin_ids


@router.message(Command("admin"))
async def cmd_admin(message: Message):
    if not is_admin(message.from_user.id):
        await message.answer("🚫 Нет доступа.")
        return

    await message.answer(
        "🔧 <b>Панель администратора</b>",
        parse_mode="HTML",
        reply_markup=admin_keyboard(),
    )


@router.callback_query(F.data == "admin:stats")
async def handle_admin_stats(callback: CallbackQuery):
    if not is_admin(callback.from_user.id):
        await callback.answer("Нет доступа", show_alert=True)
        return

    async with async_session() as session:
        stats = await get_total_stats(session)

    await callback.message.edit_text(
        "📊 <b>Статистика NoirWings</b>\n\n"
        f"👥 Пользователей: {stats['total_users']}\n"
        f"📦 Всего задач: {stats['total_jobs']}\n"
        f"✅ Успешных: {stats['completed_jobs']}\n"
        f"⏱ Среднее время: {stats['avg_duration_ms']}ms",
        parse_mode="HTML",
        reply_markup=admin_keyboard(),
    )
    await callback.answer()


@router.message(Command("whitelist"))
async def cmd_whitelist(message: Message):
    if not is_admin(message.from_user.id):
        await message.answer("🚫 Нет доступа.")
        return

    parts = message.text.split()
    if len(parts) < 3:
        await message.answer(
            "📋 <b>Использование:</b>\n"
            "<code>/whitelist add &lt;telegram_id&gt;</code>\n"
            "<code>/whitelist remove &lt;telegram_id&gt;</code>",
            parse_mode="HTML",
        )
        return

    action = parts[1].lower()
    try:
        target_id = int(parts[2])
    except ValueError:
        await message.answer("⚠️ Укажите числовой Telegram ID.")
        return

    async with async_session() as session:
        if action == "add":
            success = await add_to_whitelist(session, target_id, message.from_user.id)
            if success:
                await message.answer(f"✅ Пользователь <code>{target_id}</code> добавлен в whitelist.", parse_mode="HTML")
            else:
                await message.answer("⚠️ Пользователь уже в whitelist.")
        elif action == "remove":
            success = await remove_from_whitelist(session, target_id)
            if success:
                await message.answer(f"✅ Пользователь <code>{target_id}</code> удалён из whitelist.", parse_mode="HTML")
            else:
                await message.answer("⚠️ Пользователь не найден в whitelist.")
        else:
            await message.answer("⚠️ Используйте <code>add</code> или <code>remove</code>.", parse_mode="HTML")


@router.message(Command("grant"))
async def cmd_grant(message: Message):
    if not is_admin(message.from_user.id):
        await message.answer("🚫 Нет доступа.")
        return

    parts = message.text.split()
    if len(parts) < 4:
        await message.answer(
            "📋 <b>Использование:</b>\n"
            "<code>/grant &lt;telegram_id&gt; &lt;free|pro|unlimited&gt; &lt;days&gt;</code>",
            parse_mode="HTML",
        )
        return

    try:
        target_id = int(parts[1])
        plan = Plan(parts[2].lower())
        days = int(parts[3])
    except (ValueError, KeyError):
        await message.answer("⚠️ Неверные параметры. Пример: /grant 123456 pro 30")
        return

    async with async_session() as session:
        success = await grant_subscription(session, target_id, plan, days)

    if success:
        await message.answer(
            f"✅ Подписка <b>{plan.value.upper()}</b> выдана пользователю "
            f"<code>{target_id}</code> на {days} дней.",
            parse_mode="HTML",
        )
    else:
        await message.answer("⚠️ Пользователь не найден.")


@router.message(Command("stats"))
async def cmd_stats(message: Message):
    if not is_admin(message.from_user.id):
        await message.answer("🚫 Нет доступа.")
        return

    async with async_session() as session:
        stats = await get_total_stats(session)

    await message.answer(
        "📊 <b>Статистика NoirWings</b>\n\n"
        f"👥 Пользователей: {stats['total_users']}\n"
        f"📦 Всего задач: {stats['total_jobs']}\n"
        f"✅ Успешных: {stats['completed_jobs']}\n"
        f"⏱ Среднее время: {stats['avg_duration_ms']}ms",
        parse_mode="HTML",
    )
