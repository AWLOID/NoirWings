from typing import Any, Awaitable, Callable, Dict

from aiogram import BaseMiddleware
from aiogram.types import Message, TelegramObject

from bot.config import settings
from bot.db.crud import is_whitelisted
from bot.db.session import async_session


class AuthMiddleware(BaseMiddleware):
    """Check that the user is whitelisted before processing messages."""

    # Commands that don't require whitelist
    BYPASS_COMMANDS = {"/start"}

    async def __call__(
        self,
        handler: Callable[[TelegramObject, Dict[str, Any]], Awaitable[Any]],
        event: TelegramObject,
        data: Dict[str, Any],
    ) -> Any:
        if not isinstance(event, Message):
            return await handler(event, data)

        # Allow /start always (so new users can see access denied message)
        if event.text and event.text.split()[0] in self.BYPASS_COMMANDS:
            return await handler(event, data)

        # Admin bypass
        if event.from_user and event.from_user.id in settings.admin_ids:
            return await handler(event, data)

        # Check whitelist
        if event.from_user:
            async with async_session() as session:
                if await is_whitelisted(session, event.from_user.id):
                    return await handler(event, data)

        # Not authorized — silently ignore or send minimal response
        await event.answer("🚫 Доступ ограничен. Обратитесь к администратору.")
        return None
