from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton


def options_keyboard(current: dict) -> InlineKeyboardMarkup:
    """Build inline keyboard for toggling obfuscation options."""

    options = [
        ("opaque_predicates", "Opaque Predicates"),
        ("environment_cage", "Environment Cage"),
        ("anti_hook", "Anti-Hook"),
        ("dynamic_dispatch", "Dynamic Dispatch"),
        ("watermark_integrity", "Watermark Integrity"),
        ("inner_string_encryption", "String Encryption"),
    ]

    rows = []
    for key, label in options:
        enabled = current.get(key, False)
        emoji = "✅" if enabled else "❌"
        rows.append([
            InlineKeyboardButton(text=f"{emoji} {label}", callback_data=f"opt_toggle:{key}")
        ])

    rows.append([
        InlineKeyboardButton(text="💾 Сохранить", callback_data="opt_save"),
        InlineKeyboardButton(text="🔄 Сброс", callback_data="opt_reset"),
    ])

    return InlineKeyboardMarkup(inline_keyboard=rows)
