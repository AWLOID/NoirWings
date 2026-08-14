from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton


def profile_keyboard() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [
            InlineKeyboardButton(text="⚡ Balanced", callback_data="profile:balanced"),
            InlineKeyboardButton(text="🛡 Hardened", callback_data="profile:hardened"),
        ],
        [
            InlineKeyboardButton(text="🔒 Maximum", callback_data="profile:maximum"),
        ],
    ])


def profile_keyboard_with_default(default: str) -> InlineKeyboardMarkup:
    labels = {
        "balanced": "⚡ Balanced",
        "hardened": "🛡 Hardened",
        "maximum": "🔒 Maximum",
    }
    buttons = []
    for key, label in labels.items():
        text = f"✅ {label}" if key == default else label
        buttons.append(InlineKeyboardButton(text=text, callback_data=f"set_profile:{key}"))

    return InlineKeyboardMarkup(inline_keyboard=[buttons[:2], buttons[2:]])
