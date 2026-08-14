from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton


def admin_keyboard() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [
            InlineKeyboardButton(text="📊 Статистика", callback_data="admin:stats"),
            InlineKeyboardButton(text="👥 Whitelist", callback_data="admin:whitelist"),
        ],
        [
            InlineKeyboardButton(text="🎫 Подписки", callback_data="admin:subs"),
            InlineKeyboardButton(text="📋 Последние задачи", callback_data="admin:jobs"),
        ],
    ])
