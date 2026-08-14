import time
from collections import defaultdict
from typing import Any, Awaitable, Callable, Dict

from aiogram import BaseMiddleware
from aiogram.types import Message, TelegramObject


class ThrottleMiddleware(BaseMiddleware):
    """Simple per-user rate limiting."""

    def __init__(self, rate_limit: float = 2.0):
        """
        Args:
            rate_limit: Minimum seconds between messages from the same user.
        """
        self.rate_limit = rate_limit
        self._last_message: Dict[int, float] = defaultdict(float)

    async def __call__(
        self,
        handler: Callable[[TelegramObject, Dict[str, Any]], Awaitable[Any]],
        event: TelegramObject,
        data: Dict[str, Any],
    ) -> Any:
        if not isinstance(event, Message) or not event.from_user:
            return await handler(event, data)

        user_id = event.from_user.id
        now = time.monotonic()
        last = self._last_message[user_id]

        if now - last < self.rate_limit:
            # Too fast — ignore silently
            return None

        self._last_message[user_id] = now
        return await handler(event, data)
