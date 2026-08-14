import json
from datetime import datetime, timedelta, timezone

from sqlalchemy import select, func, delete
from sqlalchemy.ext.asyncio import AsyncSession

from bot.db.models import (
    User,
    UserRole,
    Subscription,
    Plan,
    Whitelist,
    ObfuscationJob,
    JobStatus,
)


# ─── Users ───────────────────────────────────────────────────────────────────


async def get_or_create_user(
    session: AsyncSession, telegram_id: int, username: str | None = None, first_name: str | None = None
) -> User:
    stmt = select(User).where(User.telegram_id == telegram_id)
    result = await session.execute(stmt)
    user = result.scalar_one_or_none()
    if user is None:
        user = User(telegram_id=telegram_id, username=username, first_name=first_name)
        session.add(user)
        await session.flush()
        # Create free subscription by default (3 obfuscations per day)
        sub = Subscription(user_id=user.id, plan=Plan.free, daily_limit=3)
        session.add(sub)
        await session.commit()
        await session.refresh(user)
    else:
        if username and user.username != username:
            user.username = username
        if first_name and user.first_name != first_name:
            user.first_name = first_name
        await session.commit()
    return user


async def get_user_by_telegram_id(session: AsyncSession, telegram_id: int) -> User | None:
    stmt = select(User).where(User.telegram_id == telegram_id)
    result = await session.execute(stmt)
    return result.scalar_one_or_none()


# ─── Whitelist ───────────────────────────────────────────────────────────────


async def is_whitelisted(session: AsyncSession, telegram_id: int) -> bool:
    stmt = (
        select(Whitelist)
        .join(User)
        .where(User.telegram_id == telegram_id)
    )
    result = await session.execute(stmt)
    return result.scalar_one_or_none() is not None


async def add_to_whitelist(session: AsyncSession, telegram_id: int, added_by: int) -> bool:
    user = await get_user_by_telegram_id(session, telegram_id)
    if user is None:
        user = User(telegram_id=telegram_id)
        session.add(user)
        await session.flush()
        sub = Subscription(user_id=user.id, plan=Plan.free, daily_limit=5)
        session.add(sub)

    existing = await session.execute(
        select(Whitelist).where(Whitelist.user_id == user.id)
    )
    if existing.scalar_one_or_none():
        return False

    wl = Whitelist(user_id=user.id, added_by=added_by)
    session.add(wl)
    await session.commit()
    return True


async def remove_from_whitelist(session: AsyncSession, telegram_id: int) -> bool:
    user = await get_user_by_telegram_id(session, telegram_id)
    if user is None:
        return False
    stmt = delete(Whitelist).where(Whitelist.user_id == user.id)
    result = await session.execute(stmt)
    await session.commit()
    return result.rowcount > 0


# ─── Subscriptions ───────────────────────────────────────────────────────────


async def get_subscription(session: AsyncSession, telegram_id: int) -> Subscription | None:
    stmt = (
        select(Subscription)
        .join(User)
        .where(User.telegram_id == telegram_id)
    )
    result = await session.execute(stmt)
    return result.scalar_one_or_none()


async def grant_subscription(
    session: AsyncSession, telegram_id: int, plan: Plan, days: int
) -> bool:
    user = await get_user_by_telegram_id(session, telegram_id)
    if user is None:
        return False

    stmt = select(Subscription).where(Subscription.user_id == user.id)
    result = await session.execute(stmt)
    sub = result.scalar_one_or_none()

    limits = {Plan.free: 3, Plan.pro: 50, Plan.unlimited: 999999}

    if sub is None:
        sub = Subscription(
            user_id=user.id,
            plan=plan,
            daily_limit=limits.get(plan, 5),
            expires_at=datetime.now(timezone.utc) + timedelta(days=days) if days > 0 else None,
        )
        session.add(sub)
    else:
        sub.plan = plan
        sub.daily_limit = limits.get(plan, 5)
        sub.expires_at = datetime.now(timezone.utc) + timedelta(days=days) if days > 0 else None

    await session.commit()
    return True


# ─── Jobs ────────────────────────────────────────────────────────────────────


async def create_job(
    session: AsyncSession,
    user_id: int,
    input_filename: str,
    profile: str,
    options: dict | None = None,
) -> ObfuscationJob:
    job = ObfuscationJob(
        user_id=user_id,
        input_filename=input_filename,
        profile=profile,
        options_json=json.dumps(options) if options else None,
        status=JobStatus.pending,
    )
    session.add(job)
    await session.commit()
    await session.refresh(job)
    return job


async def complete_job(
    session: AsyncSession, job_id: int, output_size: int, duration_ms: int
) -> None:
    stmt = select(ObfuscationJob).where(ObfuscationJob.id == job_id)
    result = await session.execute(stmt)
    job = result.scalar_one()
    job.status = JobStatus.completed
    job.output_size = output_size
    job.duration_ms = duration_ms
    await session.commit()


async def fail_job(session: AsyncSession, job_id: int, error: str) -> None:
    stmt = select(ObfuscationJob).where(ObfuscationJob.id == job_id)
    result = await session.execute(stmt)
    job = result.scalar_one()
    job.status = JobStatus.failed
    job.error_message = error
    await session.commit()


async def get_user_jobs(
    session: AsyncSession, telegram_id: int, limit: int = 10
) -> list[ObfuscationJob]:
    stmt = (
        select(ObfuscationJob)
        .join(User)
        .where(User.telegram_id == telegram_id)
        .order_by(ObfuscationJob.created_at.desc())
        .limit(limit)
    )
    result = await session.execute(stmt)
    return list(result.scalars().all())


async def count_today_jobs(session: AsyncSession, telegram_id: int) -> int:
    today_start = datetime.now(timezone.utc).replace(hour=0, minute=0, second=0, microsecond=0)
    stmt = (
        select(func.count(ObfuscationJob.id))
        .join(User)
        .where(User.telegram_id == telegram_id)
        .where(ObfuscationJob.created_at >= today_start)
        .where(ObfuscationJob.status != JobStatus.failed)
    )
    result = await session.execute(stmt)
    return result.scalar() or 0


# ─── Stats ───────────────────────────────────────────────────────────────────


async def get_total_stats(session: AsyncSession) -> dict:
    total_users = await session.execute(select(func.count(User.id)))
    total_jobs = await session.execute(select(func.count(ObfuscationJob.id)))
    completed_jobs = await session.execute(
        select(func.count(ObfuscationJob.id)).where(ObfuscationJob.status == JobStatus.completed)
    )
    avg_duration = await session.execute(
        select(func.avg(ObfuscationJob.duration_ms)).where(ObfuscationJob.status == JobStatus.completed)
    )
    return {
        "total_users": total_users.scalar() or 0,
        "total_jobs": total_jobs.scalar() or 0,
        "completed_jobs": completed_jobs.scalar() or 0,
        "avg_duration_ms": int(avg_duration.scalar() or 0),
    }
