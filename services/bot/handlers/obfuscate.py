import io

from aiogram import F, Router
from aiogram.types import BufferedInputFile, CallbackQuery, Message
from sqlalchemy.ext.asyncio import AsyncSession

from bot.db.crud import (
    count_today_jobs,
    create_job,
    complete_job,
    fail_job,
    get_or_create_user,
    get_subscription,
    is_whitelisted,
)
from bot.db.session import async_session
from bot.keyboards.profile import profile_keyboard
from bot.services.api_client import NoirWingsAPIClient

router = Router()

# Temporary storage for pending files (in production, use Redis or FSM)
_pending_files: dict[int, tuple[str, bytes]] = {}


@router.message(F.document)
async def handle_document(message: Message):
    """Handle uploaded .lua file."""
    doc = message.document

    if not doc.file_name or not doc.file_name.endswith(".lua"):
        await message.answer("⚠️ Пожалуйста, отправьте файл с расширением <code>.lua</code>", parse_mode="HTML")
        return

    async with async_session() as session:
        if not await is_whitelisted(session, message.from_user.id):
            await message.answer("🚫 Доступ ограничен.")
            return

        sub = await get_subscription(session, message.from_user.id)
        if sub:
            today_count = await count_today_jobs(session, message.from_user.id)
            if today_count >= sub.daily_limit:
                await message.answer(
                    f"⚠️ Достигнут дневной лимит ({sub.daily_limit} обфускаций).\n"
                    "Повысьте подписку для увеличения лимита.",
                )
                return

    # Download file
    file = await message.bot.get_file(doc.file_id)
    file_data = io.BytesIO()
    await message.bot.download_file(file.file_path, file_data)
    file_bytes = file_data.getvalue()

    if len(file_bytes) > 512 * 1024:
        await message.answer("⚠️ Файл слишком большой. Максимум 512 КБ.")
        return

    # Store file and ask for profile
    _pending_files[message.from_user.id] = (doc.file_name, file_bytes)

    await message.answer(
        f"📄 Файл <code>{doc.file_name}</code> получен ({len(file_bytes)} байт).\n\n"
        "Выберите профиль защиты:",
        parse_mode="HTML",
        reply_markup=profile_keyboard(),
    )


@router.callback_query(F.data.startswith("profile:"))
async def handle_profile_choice(callback: CallbackQuery):
    """Handle profile selection and run obfuscation."""
    profile = callback.data.split(":")[1]
    user_id = callback.from_user.id

    pending = _pending_files.pop(user_id, None)
    if pending is None:
        await callback.answer("⚠️ Файл не найден. Отправьте .lua файл заново.", show_alert=True)
        return

    filename, file_bytes = pending
    await callback.message.edit_text(
        f"⏳ Обфускация <code>{filename}</code> профилем <b>{profile}</b>...\n"
        "Это может занять до 2 минут.",
        parse_mode="HTML",
    )
    await callback.answer()

    async with async_session() as session:
        user = await get_or_create_user(session, user_id)
        job = await create_job(session, user.id, filename, profile)

    # Call the API
    client = NoirWingsAPIClient()
    try:
        result = await client.obfuscate(file_bytes, filename, profile)

        if result["success"]:
            output_data = result["data"]
            output_size = len(output_data)
            duration_ms = result.get("duration_ms", 0)

            async with async_session() as session:
                await complete_job(session, job.id, output_size, duration_ms)

            # Send obfuscated file back
            output_file = BufferedInputFile(
                output_data,
                filename=f"obf_{filename}",
            )
            await callback.message.answer_document(
                output_file,
                caption=(
                    f"✅ <b>Обфускация завершена</b>\n\n"
                    f"📄 Файл: <code>{filename}</code>\n"
                    f"🛡 Профиль: <b>{profile}</b>\n"
                    f"📊 Размер: {output_size:,} байт\n"
                    f"⏱ Время: {duration_ms}ms"
                ),
                parse_mode="HTML",
            )
            await callback.message.delete()
        else:
            error = result.get("error", "Unknown error")
            async with async_session() as session:
                await fail_job(session, job.id, error)

            await callback.message.edit_text(
                f"❌ <b>Ошибка обфускации</b>\n\n"
                f"<code>{error[:500]}</code>",
                parse_mode="HTML",
            )
    except Exception as e:
        async with async_session() as session:
            await fail_job(session, job.id, str(e))

        await callback.message.edit_text(
            f"❌ <b>Ошибка соединения с API</b>\n\n<code>{str(e)[:300]}</code>",
            parse_mode="HTML",
        )
    finally:
        await client.close()
