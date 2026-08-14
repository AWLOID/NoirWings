from typing import Any, Awaitable, Callable, Dict

from aiogram import BaseMiddleware
from aiogram.types import Message, CallbackQuery, TelegramObject

from bot.config import settings
from bot.db.crud import get_or_create_user
from bot.db.session import async_session


class AuthMiddleware(BaseMiddleware):
    """Auto-register users on first interaction. No whitelist blocking."""

    async def __call__(
        self,
        handler: Callable[[TelegramObject, Dict[str, Any]], Awaitable[Any]],
        event: TelegramObject,
        data: Dict[str, Any],
    ) -> Any:
        # Get user from event
        user = None
        if isinstance(event, Message) and event.from_user:
            user = event.from_user
        elif isinstance(event, CallbackQuery) and event.from_user:
            user = event.from_user

        # Auto-register user in DB
        if user:
            async with async_session() as session:
                db_user = await get_or_create_user(
                    session,
                    telegram_id=user.id,
                    username=user.username,
                    first_name=user.first_name,
                )
                data["db_user"] = db_user
                data["is_admin"] = user.id in settings.admin_ids

        return await handler(event, data)
