from aiogram import Router
from aiogram.filters import Command
from aiogram.types import Message

from bot.db.crud import get_user_jobs
from bot.db.session import async_session

router = Router()


@router.message(Command("history"))
async def cmd_history(message: Message):
    async with async_session() as session:
        jobs = await get_user_jobs(session, message.from_user.id, limit=10)

    if not jobs:
        await message.answer("📭 История обфускаций пуста.")
        return

    lines = ["📋 <b>Последние обфускации:</b>\n"]
    for i, job in enumerate(jobs, 1):
        status_emoji = {
            "completed": "✅",
            "failed": "❌",
            "pending": "⏳",
            "processing": "⚙️",
        }
        emoji = status_emoji.get(job.status.value, "❓")
        duration = f"{job.duration_ms}ms" if job.duration_ms else "—"
        size = f"{job.output_size:,}B" if job.output_size else "—"
        date = job.created_at.strftime("%d.%m %H:%M")

        lines.append(
            f"{i}. {emoji} <code>{job.input_filename}</code>\n"
            f"   {job.profile} | {size} | {duration} | {date}"
        )

    await message.answer("\n".join(lines), parse_mode="HTML")
