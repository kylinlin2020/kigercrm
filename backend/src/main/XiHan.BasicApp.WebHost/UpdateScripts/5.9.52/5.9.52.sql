-- 5.9.52: tenant-scoped sales-exam question bank, papers and attempts. Correct answers and
-- explanations never leave the application layer in candidate-facing DTOs.
CREATE TABLE IF NOT EXISTS crm_exam_question
(
    basic_id bigint NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    tenant_id bigint NOT NULL,
    created_time timestamp with time zone NOT NULL,
    created_id bigint NULL,
    created_by text NULL,
    modified_time timestamp with time zone NULL,
    modified_id bigint NULL,
    modified_by text NULL,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_time timestamp with time zone NULL,
    deleted_id bigint NULL,
    deleted_by text NULL,
    prompt text NOT NULL,
    type integer NOT NULL DEFAULT 0,
    options_json text NOT NULL DEFAULT '[]',
    correct_answer varchar(40) NOT NULL,
    explanation varchar(1000) NULL,
    points integer NOT NULL DEFAULT 1,
    CONSTRAINT pk_crm_exam_question PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_exam_question_te_crti
    ON crm_exam_question (tenant_id, created_time DESC);

CREATE TABLE IF NOT EXISTS crm_exam_paper
(
    basic_id bigint NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    tenant_id bigint NOT NULL,
    created_time timestamp with time zone NOT NULL,
    created_id bigint NULL,
    created_by text NULL,
    modified_time timestamp with time zone NULL,
    modified_id bigint NULL,
    modified_by text NULL,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_time timestamp with time zone NULL,
    deleted_id bigint NULL,
    deleted_by text NULL,
    name varchar(160) NOT NULL,
    question_ids_json text NOT NULL DEFAULT '[]',
    duration_minutes integer NOT NULL DEFAULT 30,
    pass_score integer NOT NULL DEFAULT 60,
    max_attempts integer NOT NULL DEFAULT 3,
    status integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_crm_exam_paper PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_exam_paper_te_st_crti
    ON crm_exam_paper (tenant_id, status, created_time DESC);

CREATE TABLE IF NOT EXISTS crm_exam_attempt
(
    basic_id bigint NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    tenant_id bigint NOT NULL,
    created_time timestamp with time zone NOT NULL,
    created_id bigint NULL,
    created_by text NULL,
    modified_time timestamp with time zone NULL,
    modified_id bigint NULL,
    modified_by text NULL,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_time timestamp with time zone NULL,
    deleted_id bigint NULL,
    deleted_by text NULL,
    paper_id bigint NOT NULL,
    user_id bigint NOT NULL,
    status integer NOT NULL DEFAULT 0,
    started_at_utc timestamp with time zone NOT NULL,
    submitted_at_utc timestamp with time zone NULL,
    answers_json text NOT NULL DEFAULT '{}',
    score integer NOT NULL DEFAULT 0,
    total_score integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_crm_exam_attempt PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_exam_attempt_te_pa_us
    ON crm_exam_attempt (tenant_id, paper_id, user_id);
