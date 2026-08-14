import asyncio
import logging

from aiogram import Bot, Dispatcher
from aiogram.client.default import DefaultBotProperties
from aiogram.enums import ParseMode

from bot.config import settings
from bot.db.models import Base
from bot.db.session import engine
from bot.handlers.admin import router as admin_router
from bot.handlers.history import router as history_router
from bot.handlers.obfuscate import router as obfuscate_router
from bot.handlers.settings import router as settings_router
from bot.handlers.start import router as start_router
from bot.middlewares.auth import AuthMiddleware
from bot.middlewares.throttle import ThrottleMiddleware

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)


async def on_startup():
    """Create database tables on startup."""
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    logger.info("Database tables created/verified.")


async def main():
    bot = Bot(token=settings.bot_token, default=DefaultBotProperties(parse_mode=ParseMode.HTML))
    dp = Dispatcher()

    # Register middlewares
    dp.message.middleware(ThrottleMiddleware(rate_limit=1.5))
    dp.message.middleware(AuthMiddleware())

    # Register routers
    dp.include_router(start_router)
    dp.include_router(obfuscate_router)
    dp.include_router(settings_router)
    dp.include_router(admin_router)
    dp.include_router(history_router)

    # Startup hook
    await on_startup()

    logger.info("NoirWings Bot started.")
    await dp.start_polling(bot)


if __name__ == "__main__":
    asyncio.run(main())
