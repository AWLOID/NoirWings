from aiogram import F, Router
from aiogram.filters import Command
from aiogram.types import CallbackQuery, Message

from bot.db.crud import get_or_create_user
from bot.db.session import async_session
from bot.keyboards.options import options_keyboard
from bot.keyboards.profile import profile_keyboard_with_default

router = Router()

# Default options per user (in production use DB or Redis)
_user_options: dict[int, dict] = {}

DEFAULT_OPTIONS = {
    "opaque_predicates": True,
    "environment_cage": True,
    "anti_hook": True,
    "dynamic_dispatch": False,
    "watermark_integrity": True,
    "inner_string_encryption": False,
}


def get_user_options(user_id: int) -> dict:
    if user_id not in _user_options:
        _user_options[user_id] = DEFAULT_OPTIONS.copy()
    return _user_options[user_id]


@router.message(Command("profile"))
async def cmd_profile(message: Message):
    async with async_session() as session:
        user = await get_or_create_user(
            session,
            telegram_id=message.from_user.id,
            username=message.from_user.username,
        )
        default = user.default_profile

    await message.answer(
        "🎯 <b>Профиль защиты по умолчанию</b>\n\n"
        "Выберите профиль, который будет использоваться при обфускации:",
        parse_mode="HTML",
        reply_markup=profile_keyboard_with_default(default),
    )


@router.callback_query(F.data.startswith("set_profile:"))
async def handle_set_profile(callback: CallbackQuery):
    profile = callback.data.split(":")[1]

    async with async_session() as session:
        user = await get_or_create_user(session, callback.from_user.id)
        user.default_profile = profile
        await session.commit()

    await callback.message.edit_text(
        f"✅ Профиль по умолчанию изменён на <b>{profile}</b>",
        parse_mode="HTML",
    )
    await callback.answer("Сохранено!")


@router.message(Command("options"))
async def cmd_options(message: Message):
    opts = get_user_options(message.from_user.id)
    await message.answer(
        "⚙️ <b>Настройка опций обфускации</b>\n\n"
        "Нажмите на опцию для переключения:",
        parse_mode="HTML",
        reply_markup=options_keyboard(opts),
    )


@router.callback_query(F.data.startswith("opt_toggle:"))
async def handle_option_toggle(callback: CallbackQuery):
    key = callback.data.split(":")[1]
    opts = get_user_options(callback.from_user.id)

    if key in opts:
        opts[key] = not opts[key]

    await callback.message.edit_reply_markup(reply_markup=options_keyboard(opts))
    await callback.answer(f"{'✅ Вкл' if opts.get(key) else '❌ Выкл'}")


@router.callback_query(F.data == "opt_save")
async def handle_options_save(callback: CallbackQuery):
    await callback.answer("💾 Настройки сохранены!")
    await callback.message.edit_text(
        "✅ <b>Настройки сохранены</b>\n\n"
        "Они будут применяться при следующей обфускации.",
        parse_mode="HTML",
    )


@router.callback_query(F.data == "opt_reset")
async def handle_options_reset(callback: CallbackQuery):
    _user_options[callback.from_user.id] = DEFAULT_OPTIONS.copy()
    opts = get_user_options(callback.from_user.id)
    await callback.message.edit_reply_markup(reply_markup=options_keyboard(opts))
    await callback.answer("🔄 Настройки сброшены!")
