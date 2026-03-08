-- ═══════════════════════════════════════════════════════════════════════════
-- SUPABASE MIGRATION – Tabelas do Microsserviço de Integração Strava
-- Execute este script no SQL Editor do seu projeto Supabase
-- ═══════════════════════════════════════════════════════════════════════════

-- ─── Extensão para UUIDs ─────────────────────────────────────────────────────
create extension if not exists "uuid-ossp";

-- ─── user_strava_tokens ──────────────────────────────────────────────────────
-- Armazena os tokens OAuth do Strava vinculados a cada usuário do Supabase.
-- Os tokens ficam em uma tabela separada para facilitar a rotação e auditoria.
create table if not exists public.user_strava_tokens (
    id                uuid        primary key default uuid_generate_v4(),
    user_id           uuid        not null references auth.users(id) on delete cascade,
    strava_athlete_id bigint      not null,
    access_token      text        not null,   -- Short-lived (~6h)
    refresh_token     text        not null,   -- Long-lived
    expires_at        bigint      not null,   -- Unix timestamp
    scope             text        not null default 'activity:read_all',
    updated_at        timestamptz not null default now(),

    constraint uq_user_strava unique (user_id)
);

-- Índice para busca por user_id (operação mais frequente)
create index if not exists idx_user_strava_tokens_user_id
    on public.user_strava_tokens (user_id);

-- ─── Row Level Security para user_strava_tokens ──────────────────────────────
alter table public.user_strava_tokens enable row level security;

-- Usuários só veem seu próprio token (via client anon key)
create policy "Usuário vê apenas o próprio token"
    on public.user_strava_tokens for select
    using (auth.uid() = user_id);

-- O microsserviço usa service_role_key → bypassa RLS automaticamente.
-- Portanto, não é necessária policy de insert/update para service role.

-- ─── challenges (já existente – adaptação mínima) ────────────────────────────
-- Apenas garante que a tabela está com o schema correto.
-- Se já existir, este bloco é seguro de executar.
create table if not exists public.challenges (
    id              uuid    primary key default uuid_generate_v4(),
    club_id         uuid    not null,
    title           text    not null,
    -- "distance_km" | "pace_min_per_km" | "elevation_m" | "duration_min"
    challenge_type  text    not null,
    target_value    numeric not null,
    created_at      timestamptz default now()
);

-- ─── rewards (já existente) ───────────────────────────────────────────────────
create table if not exists public.rewards (
    id           uuid primary key default uuid_generate_v4(),
    club_id      uuid not null,
    challenge_id uuid not null references public.challenges(id) on delete cascade,
    created_at   timestamptz default now()
);

-- ─── reward_history (já existente) ───────────────────────────────────────────
create table if not exists public.reward_history (
    id         uuid        primary key default uuid_generate_v4(),
    reward_id  uuid        not null references public.rewards(id) on delete cascade,
    user_id    uuid        not null references auth.users(id) on delete cascade,
    earned_at  timestamptz not null default now(),
    -- "strava_sync" | "manual" | etc.
    proof_type text        not null default 'strava_sync',
    -- URL da atividade Strava: https://www.strava.com/activities/{id}
    proof_url  text        not null,

    -- Impede concessão duplicada do mesmo prêmio ao mesmo usuário
    constraint uq_reward_user unique (reward_id, user_id)
);

-- Índices para queries frequentes
create index if not exists idx_reward_history_user_id
    on public.reward_history (user_id);

create index if not exists idx_reward_history_reward_id
    on public.reward_history (reward_id);

-- ─── RLS para reward_history ──────────────────────────────────────────────────
alter table public.reward_history enable row level security;

create policy "Usuário vê apenas o próprio histórico"
    on public.reward_history for select
    using (auth.uid() = user_id);

-- ─── Função auxiliar: retorna o status da conexão Strava do usuário ──────────
create or replace function public.get_strava_connection_status(p_user_id uuid)
returns table (
    connected          boolean,
    strava_athlete_id  bigint,
    token_expires_at   timestamptz
)
language sql
security definer  -- executa como owner, bypassa RLS
as $$
    select
        true                                                  as connected,
        t.strava_athlete_id,
        to_timestamp(t.expires_at)                            as token_expires_at
    from public.user_strava_tokens t
    where t.user_id = p_user_id
    union all
    select false, null, null
    where not exists (
        select 1 from public.user_strava_tokens where user_id = p_user_id
    );
$$;
